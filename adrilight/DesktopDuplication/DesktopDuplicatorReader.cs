using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using adrilight.DesktopDuplication;
using NLog;
using Polly;
using System.Linq;
using System.Windows.Media.Imaging;
using adrilight.ViewModel;
using System.Runtime.InteropServices;
using adrilight.Settings;
using adrilight.Util;

namespace adrilight
{
    internal class DesktopDuplicatorReader : IDesktopDuplicatorReader
    {
        private readonly ILogger _log = LogManager.GetCurrentClassLogger();
        private readonly IModeManager _modeManager;

        public DesktopDuplicatorReader(IUserSettings userSettings, ISpotSet spotSet, SettingsViewModel settingsViewModel, IModeManager modeManager)
        {
            UserSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            SpotSet = spotSet ?? throw new ArgumentNullException(nameof(spotSet));
            SettingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
            _modeManager = modeManager ?? throw new ArgumentNullException(nameof(modeManager));
            _retryPolicy = Policy.Handle<Exception>()
                .WaitAndRetryForever(ProvideDelayDuration);

            UserSettings.PropertyChanged += PropertyChanged;
            SettingsViewModel.PropertyChanged += PropertyChanged;
            _modeManager.PropertyChanged += PropertyChanged;
            RefreshCapturingState();

            _log.Info($"DesktopDuplicatorReader created.");
        }

        private void PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(UserSettings.AdapterIndex):
                case nameof(UserSettings.OutputIndex):
                    // Discard the primary duplicator so GetNextFrame() rebuilds it with the new indices.
                    _desktopDuplicator?.Dispose();
                    _desktopDuplicator = null;
                    RefreshCapturingState();
                    break;

                case nameof(UserSettings.SpanningEnabled):
                case nameof(UserSettings.AdapterIndex2):
                case nameof(UserSettings.OutputIndex2):
                    // Discard the secondary duplicator so GetNextFrame() rebuilds it with the new config.
                    _desktopDuplicator2?.Dispose();
                    _desktopDuplicator2 = null;
                    RefreshCapturingState();
                    break;

                case nameof(UserSettings.TransferActive):
                case nameof(UserSettings.IsPreviewEnabled):
                case nameof(SettingsViewModel.IsSettingsWindowOpen):
                case nameof(SettingsViewModel.IsPreviewTabOpen):
                case nameof(IModeManager.ActiveMode):
                    RefreshCapturingState();
                    break;
            }
        }

        public RunStateEnum RunState { get; private set; } = RunStateEnum.Stopped;
        private CancellationTokenSource _cancellationTokenSource;

        private void RefreshCapturingState()
        {
            var isRunning = _cancellationTokenSource != null && RunState == RunStateEnum.Running;
            var shouldBeRunning = _modeManager.ActiveMode == LightingMode.ScreenCapture
                    && (UserSettings.TransferActive
                        || SettingsViewModel.IsSettingsWindowOpen && SettingsViewModel.IsPreviewTabOpen);

            if (isRunning && !shouldBeRunning)
            {
                _log.Debug("stopping the capturing");
                RunState = RunStateEnum.Stopping;
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource = null;
            }
            else if (!isRunning && shouldBeRunning)
            {
                _log.Debug("starting the capturing");
                _cancellationTokenSource = new CancellationTokenSource();
                var thread = new Thread(() => Run(_cancellationTokenSource.Token))
                {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal,
                    Name = "DesktopDuplicatorReader"
                };
                thread.Start();
            }
        }

        private IUserSettings UserSettings { get; }
        private ISpotSet SpotSet { get; }
        private SettingsViewModel SettingsViewModel { get; }

        private readonly Policy _retryPolicy;

        private TimeSpan ProvideDelayDuration(int index)
        {
            if (index < 10)
                return TimeSpan.FromMilliseconds(100);

            if (index < 10 + 256)
            {
                SpotSet.IndicateMissingValues();
                return TimeSpan.FromMilliseconds(5000d / 256);
            }
            return TimeSpan.FromMilliseconds(1000);
        }

        private DesktopDuplicator _desktopDuplicator;
        private DesktopDuplicator _desktopDuplicator2;
        private Bitmap _rawBitmap1;
        private Bitmap _rawBitmap2;
        private Bitmap _stitchedBitmap;

        public async void Run(CancellationToken token)
        {
            while (RunState == RunStateEnum.Stopping)
                await Task.Yield();

            if (RunState != RunStateEnum.Stopped)
                throw new Exception(nameof(DesktopDuplicatorReader) + " is already running!");

            RunState = RunStateEnum.Running;
            _log.Debug("Started Desktop Duplication Reader.");
            Bitmap image = null;

            try
            {
                BitmapData bitmapData = new BitmapData();

                while (!token.IsCancellationRequested)
                {
                    var frameTime = Stopwatch.StartNew();
                    var context = new Context();
                    context.Add("image", image);
                    var newImage = _retryPolicy.Execute(c => GetNextFrame(c["image"] as Bitmap), context);
                    TraceFrameDetails(newImage);
                    if (newImage == null)
                        continue;

                    image = newImage;

                    bool isPreviewRunning = SettingsViewModel.IsSettingsWindowOpen && SettingsViewModel.IsPreviewTabOpen;
                    if (isPreviewRunning)
                        SettingsViewModel.SetPreviewImage(image);

                    image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb, bitmapData);

                    // Detect black bars once per frame — result is reused across all spot samples
                    var activeRegion = UserSettings.BlackBarDetectionEnabled
                        ? DetectBlackBars(bitmapData, image.Width, image.Height, UserSettings.BlackBarLuminanceThreshold)
                        : new Rectangle(0, 0, image.Width, image.Height);

                    lock (SpotSet.Lock)
                    {
                        var useLinearLighting = UserSettings.UseLinearLighting;

                        if (image.Width != SpotSet.ExpectedScreenWidth || image.Height != SpotSet.ExpectedScreenHeight)
                        {
                            SpotSet.IndicateMissingValues();
                            return;
                        }

                        const int parallelThreshold = 40;
                        if (SpotSet.Spots.Length >= parallelThreshold)
                        {
                            Parallel.ForEach(SpotSet.Spots, spot =>
                            {
                                ProcessSpot(spot, bitmapData, useLinearLighting, isPreviewRunning, activeRegion);
                            });
                        }
                        else
                        {
                            foreach (var spot in SpotSet.Spots)
                            {
                                ProcessSpot(spot, bitmapData, useLinearLighting, isPreviewRunning, activeRegion);
                            }
                        }

                        SpotSet.IsDirty = true;

                        if (isPreviewRunning)
                            SettingsViewModel.PreviewSpots = SpotSet.Spots;
                    }

                    image.UnlockBits(bitmapData);

                    int minFrameTimeInMs = 1000 / UserSettings.LimitFps;
                    var elapsedMs = (int)frameTime.ElapsedMilliseconds;
                    if (elapsedMs < minFrameTimeInMs)
                        Thread.Sleep(minFrameTimeInMs - elapsedMs);
                }
            }
            finally
            {
                image?.Dispose();
                _rawBitmap1?.Dispose(); _rawBitmap1 = null;
                _rawBitmap2?.Dispose(); _rawBitmap2 = null;
                _stitchedBitmap = null; // already disposed via image above when spanning was active
                _desktopDuplicator?.Dispose();  _desktopDuplicator  = null;
                _desktopDuplicator2?.Dispose(); _desktopDuplicator2 = null;
                _log.Debug("Stopped Desktop Duplication Reader.");
                RunState = RunStateEnum.Stopped;
            }
        }

        private void ProcessSpot(ISpot spot, BitmapData bitmapData, bool useLinearLighting, bool isPreviewRunning, Rectangle activeRegion)
        {
            // Remap spot to nearest content edge when it falls in a black bar region
            var samplingRect = GetSamplingRectangle(spot.Rectangle, activeRegion);

            const int numberOfSteps = 15;
            int stepx = Math.Max(1, samplingRect.Width / numberOfSteps);
            int stepy = Math.Max(1, samplingRect.Height / numberOfSteps);

            GetAverageColorOfRectangularRegion(samplingRect, stepy, stepx, bitmapData,
                out int sumR, out int sumG, out int sumB, out int count);

            var countInverse = 1f / count;

            ApplyColorCorrections(sumR * countInverse, sumG * countInverse, sumB * countInverse,
                out byte finalR, out byte finalG, out byte finalB, useLinearLighting,
                UserSettings.SaturationTreshold, spot.Red, spot.Green, spot.Blue);

            spot.SetColor(finalR, finalG, finalB, isPreviewRunning);
        }

        private int? _lastObservedHeight;
        private int? _lastObservedWidth;

        private void TraceFrameDetails(Bitmap image)
        {
            if (image != null)
            {
                if (_lastObservedHeight != null && _lastObservedWidth != null
                    && (_lastObservedHeight != image.Height || _lastObservedWidth != image.Width))
                {
                    _log.Debug("The frame size changed from {0}x{1} to {2}x{3}",
                        _lastObservedWidth, _lastObservedHeight, image.Width, image.Height);
                }
                _lastObservedWidth = image.Width;
                _lastObservedHeight = image.Height;
            }
        }

        private void ApplyColorCorrections(float r, float g, float b, out byte finalR, out byte finalG, out byte finalB,
            bool useLinearLighting, byte saturationTreshold, byte lastColorR, byte lastColorG, byte lastColorB)
        {
            if (lastColorR == 0 && lastColorG == 0 && lastColorB == 0)
                saturationTreshold += 2;

            if (r <= saturationTreshold && g <= saturationTreshold && b <= saturationTreshold)
            {
                finalR = finalG = finalB = 0;
                return;
            }

            var useAlternateWhiteBalance = UserSettings.AlternateWhiteBalanceMode == AlternateWhiteBalanceModeEnum.On
                || UserSettings.AlternateWhiteBalanceMode == AlternateWhiteBalanceModeEnum.Auto && SettingsViewModel.IsInNightLightMode;

            if (!useAlternateWhiteBalance)
            {
                r *= UserSettings.WhitebalanceRed / 100f;
                g *= UserSettings.WhitebalanceGreen / 100f;
                b *= UserSettings.WhitebalanceBlue / 100f;
            }
            else
            {
                r *= UserSettings.AltWhitebalanceRed / 100f;
                g *= UserSettings.AltWhitebalanceGreen / 100f;
                b *= UserSettings.AltWhitebalanceBlue / 100f;
            }

            if (!useLinearLighting)
            {
                finalR = FadeNonLinear(r);
                finalG = FadeNonLinear(g);
                finalB = FadeNonLinear(b);
            }
            else
            {
                finalR = (byte)r;
                finalG = (byte)g;
                finalB = (byte)b;
            }
        }

        private readonly byte[] _nonLinearFadingCache = Enumerable.Range(0, 2560)
            .Select(n => FadeNonLinearUncached(n / 10f))
            .ToArray();

        private byte FadeNonLinear(float color)
        {
            var cacheIndex = (int)(color * 10);
            return _nonLinearFadingCache[Math.Min(2560 - 1, Math.Max(0, cacheIndex))];
        }

        private static byte FadeNonLinearUncached(float color)
        {
            const float factor = 80f;
            return (byte)(256f * ((float)Math.Pow(factor, color / 256f) - 1f) / (factor - 1));
        }

        private Bitmap GetNextFrame(Bitmap reusableBitmap)
        {
            if (_desktopDuplicator == null)
                _desktopDuplicator = new DesktopDuplicator(UserSettings.AdapterIndex, UserSettings.OutputIndex);

            if (!UserSettings.SpanningEnabled)
            {
                try
                {
                    return _desktopDuplicator.GetLatestFrame(reusableBitmap);
                }
                catch (Exception ex)
                {
                    if (ex.Message != "_outputDuplication is null")
                        _log.Error(ex, "GetNextFrame() failed.");
                    _desktopDuplicator?.Dispose();
                    _desktopDuplicator = null;
                    throw;
                }
            }

            // Spanning: capture both monitors then stitch side-by-side.
            if (_desktopDuplicator2 == null)
                _desktopDuplicator2 = new DesktopDuplicator(UserSettings.AdapterIndex2, UserSettings.OutputIndex2);

            Bitmap frame1, frame2;
            try
            {
                frame1 = _desktopDuplicator.GetLatestFrame(_rawBitmap1);
                frame2 = _desktopDuplicator2.GetLatestFrame(_rawBitmap2);
            }
            catch (Exception ex)
            {
                if (ex.Message != "_outputDuplication is null")
                    _log.Error(ex, "GetNextFrame() spanning capture failed.");
                _desktopDuplicator?.Dispose();  _desktopDuplicator  = null;
                _desktopDuplicator2?.Dispose(); _desktopDuplicator2 = null;
                throw;
            }

            if (frame1 == null || frame2 == null)
                return null;

            _rawBitmap1 = frame1;
            _rawBitmap2 = frame2;
            return StitchBitmaps(frame1, frame2);
        }

        private Bitmap StitchBitmaps(Bitmap left, Bitmap right)
        {
            int stitchedWidth  = left.Width + right.Width;
            int stitchedHeight = Math.Max(left.Height, right.Height);

            if (_stitchedBitmap == null
                || _stitchedBitmap.Width  != stitchedWidth
                || _stitchedBitmap.Height != stitchedHeight)
            {
                _stitchedBitmap?.Dispose();
                _stitchedBitmap = new Bitmap(stitchedWidth, stitchedHeight, PixelFormat.Format32bppRgb);
            }

            int leftRowBytes  = left.Width  * 4;
            int rightRowBytes = right.Width * 4;

            var leftData     = left.LockBits(new Rectangle(0, 0, left.Width,  left.Height),  ImageLockMode.ReadOnly,  PixelFormat.Format32bppRgb);
            var rightData    = right.LockBits(new Rectangle(0, 0, right.Width, right.Height), ImageLockMode.ReadOnly,  PixelFormat.Format32bppRgb);
            var stitchedData = _stitchedBitmap.LockBits(new Rectangle(0, 0, stitchedWidth, stitchedHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);

            try
            {
                for (int y = 0; y < left.Height; y++)
                {
                    var src = IntPtr.Add(leftData.Scan0,     y * leftData.Stride);
                    var dst = IntPtr.Add(stitchedData.Scan0, y * stitchedData.Stride);
                    SharpDX.Utilities.CopyMemory(dst, src, leftRowBytes);
                }

                for (int y = 0; y < right.Height; y++)
                {
                    var src = IntPtr.Add(rightData.Scan0,    y * rightData.Stride);
                    var dst = IntPtr.Add(stitchedData.Scan0, y * stitchedData.Stride + leftRowBytes);
                    SharpDX.Utilities.CopyMemory(dst, src, rightRowBytes);
                }
            }
            finally
            {
                left.UnlockBits(leftData);
                right.UnlockBits(rightData);
                _stitchedBitmap.UnlockBits(stitchedData);
            }

            return _stitchedBitmap;
        }

        private unsafe void GetAverageColorOfRectangularRegion(Rectangle spotRectangle, int stepy, int stepx,
            BitmapData bitmapData, out int sumR, out int sumG, out int sumB, out int count)
        {
            sumR = 0;
            sumG = 0;
            sumB = 0;
            count = 0;

            var stepCount = spotRectangle.Width / stepx;
            var stepxTimes4 = stepx * 4;
            for (var y = spotRectangle.Top; y < spotRectangle.Bottom; y += stepy)
            {
                byte* pointer = (byte*)bitmapData.Scan0 + bitmapData.Stride * y + 4 * spotRectangle.Left;
                for (int i = 0; i < stepCount; i++)
                {
                    sumB += pointer[0];
                    sumG += pointer[1];
                    sumR += pointer[2];
                    pointer += stepxTimes4;
                }
                count += stepCount;
            }
        }

        /// <summary>
        /// Returns the rectangle that a spot should sample from.
        /// If the spot overlaps the active content region the intersection is used.
        /// If the spot is entirely outside (i.e. it sits over a black bar) the rectangle is
        /// clamped to the nearest edge of the active region so the LED reflects the closest
        /// real picture colour rather than sampling black pixels.
        /// </summary>
        internal static Rectangle GetSamplingRectangle(Rectangle spotRect, Rectangle activeRegion)
        {
            if (activeRegion.IsEmpty)
                return spotRect;

            int sampLeft   = Math.Max(spotRect.Left,   activeRegion.Left);
            int sampRight  = Math.Min(spotRect.Right,  activeRegion.Right);
            int sampTop    = Math.Max(spotRect.Top,    activeRegion.Top);
            int sampBottom = Math.Min(spotRect.Bottom, activeRegion.Bottom);

            // No horizontal overlap — clamp to nearest vertical edge of content
            if (sampLeft >= sampRight)
            {
                sampLeft  = spotRect.Right <= activeRegion.Left ? activeRegion.Left : activeRegion.Right - 1;
                sampRight = sampLeft + 1;
            }

            // No vertical overlap — clamp to nearest horizontal edge of content
            if (sampTop >= sampBottom)
            {
                sampTop    = spotRect.Bottom <= activeRegion.Top ? activeRegion.Top : activeRegion.Bottom - 1;
                sampBottom = sampTop + 1;
            }

            return new Rectangle(sampLeft, sampTop, sampRight - sampLeft, sampBottom - sampTop);
        }

        /// <summary>
        /// Scans the frame from each edge inward to find the active (non-black-bar) region.
        /// Sampling is sparse (5 points per row/column) so the cost is O(width + height), not O(width * height).
        /// Returns Rectangle.Empty if the entire frame is below the luminance threshold.
        /// Returns the full-frame rectangle if no bars are detected.
        /// </summary>
        internal static Rectangle DetectBlackBars(BitmapData bitmapData, int width, int height, byte luminanceThreshold)
        {
            // Scan from top — find first row with content
            int cropTop = -1;
            for (int y = 0; y < height; y++)
            {
                if (!IsRowBlack(bitmapData, y, width, luminanceThreshold))
                {
                    cropTop = y;
                    break;
                }
            }
            if (cropTop < 0)
                return Rectangle.Empty; // entire frame is black

            // Scan from bottom — find last row with content
            int cropBottom = height;
            for (int y = height - 1; y >= cropTop; y--)
            {
                if (!IsRowBlack(bitmapData, y, width, luminanceThreshold))
                {
                    cropBottom = y + 1;
                    break;
                }
            }

            // Scan from left — find first column with content
            int cropLeft = 0;
            for (int x = 0; x < width; x++)
            {
                if (!IsColumnBlack(bitmapData, x, height, luminanceThreshold))
                {
                    cropLeft = x;
                    break;
                }
            }

            // Scan from right — find last column with content
            int cropRight = width;
            for (int x = width - 1; x >= cropLeft; x--)
            {
                if (!IsColumnBlack(bitmapData, x, height, luminanceThreshold))
                {
                    cropRight = x + 1;
                    break;
                }
            }

            return Rectangle.FromLTRB(cropLeft, cropTop, cropRight, cropBottom);
        }

        // A row is black if every sampled pixel has average luminance <= threshold.
        // Samples 5 evenly-spaced pixels across the row for efficiency.
        private static unsafe bool IsRowBlack(BitmapData bitmapData, int row, int width, byte threshold)
        {
            int stepX = Math.Max(1, width / 5);
            byte* rowPtr = (byte*)bitmapData.Scan0 + bitmapData.Stride * row;
            for (int x = 0; x < width; x += stepX)
            {
                byte* pixel = rowPtr + x * 4;
                // Pixel layout is BGRA; luminance = (B + G + R) / 3
                if ((pixel[0] + pixel[1] + pixel[2]) / 3 > threshold)
                    return false;
            }
            return true;
        }

        // A column is black if every sampled pixel has average luminance <= threshold.
        // Samples 5 evenly-spaced pixels down the column for efficiency.
        private static unsafe bool IsColumnBlack(BitmapData bitmapData, int col, int height, byte threshold)
        {
            int stepY = Math.Max(1, height / 5);
            byte* colBase = (byte*)bitmapData.Scan0 + col * 4;
            for (int y = 0; y < height; y += stepY)
            {
                byte* pixel = colBase + bitmapData.Stride * y;
                if ((pixel[0] + pixel[1] + pixel[2]) / 3 > threshold)
                    return false;
            }
            return true;
        }
    }
}