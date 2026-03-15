using System;
using System.Drawing;
using System.Drawing.Imaging;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using adrilight.Extensions;
using adrilight.Util;

using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Rectangle = SharpDX.Mathematics.Interop.RawRectangle;

namespace adrilight.DesktopDuplication
{
    public class DesktopDuplicator : IDisposable
    {
        private readonly Device _device;
        private OutputDescription _outputDescription;
        private readonly OutputDuplication _outputDuplication;

        private Texture2D _stagingTexture;
        private Texture2D _smallerTexture;
        private ShaderResourceView _smallerTextureView;

        public DesktopDuplicator(int whichGraphicsCardAdapter, int whichOutputDevice)
        {
            Adapter1 adapter;
            try
            {
                adapter = new Factory1().GetAdapter1(whichGraphicsCardAdapter);
            }
            catch (SharpDXException ex)
            {
                throw new DesktopDuplicationException("Could not find the specified graphics card adapter.", ex);
            }
            _device = new Device(adapter);
            Output output;
            try
            {
                output = adapter.GetOutput(whichOutputDevice);
            }
            catch (SharpDXException ex)
            {
                throw new DesktopDuplicationException("Could not find the specified output device.", ex);
            }
            var output1 = output.QueryInterface<Output1>();
            _outputDescription = output.Description;

            try
            {
                _outputDuplication = output1.DuplicateOutput(_device);
            }
            catch (SharpDXException ex)
            {
                if (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.NotCurrentlyAvailable.Result.Code)
                {
                    throw new DesktopDuplicationException(
                        "There is already the maximum number of applications using the Desktop Duplication API running, please close one of the applications and try again.");
                }
            }
        }

        private static readonly FpsLogger _desktopFrameLogger = new FpsLogger("DesktopDuplication");

        public Bitmap GetLatestFrame(Bitmap reusableImage)
        {
            var succeeded = RetrieveFrame();
            if (!succeeded)
                return null;

            _desktopFrameLogger.TrackSingleFrame();
            return ProcessFrame(reusableImage);
        }

        private const int mipMapLevel = 3;
        private const int scalingFactor = 1 << mipMapLevel;

        private bool RetrieveFrame()
        {
            var desktopWidth = _outputDescription.DesktopBounds.GetWidth();
            var desktopHeight = _outputDescription.DesktopBounds.GetHeight();

            if (_stagingTexture == null)
            {
                _stagingTexture = new Texture2D(_device, new Texture2DDescription()
                {
                    CpuAccessFlags = CpuAccessFlags.Read,
                    BindFlags = BindFlags.None,
                    Format = Format.B8G8R8A8_UNorm,
                    Width = desktopWidth / scalingFactor,
                    Height = desktopHeight / scalingFactor,
                    OptionFlags = ResourceOptionFlags.None,
                    MipLevels = 1,
                    ArraySize = 1,
                    SampleDescription = { Count = 1, Quality = 0 },
                    Usage = ResourceUsage.Staging
                });
            }

            SharpDX.DXGI.Resource desktopResource;
            OutputDuplicateFrameInformation frameInformation;
            try
            {
                if (_outputDuplication == null) throw new Exception("_outputDuplication is null");
                _outputDuplication.AcquireNextFrame(500, out frameInformation, out desktopResource);
            }
            catch (SharpDXException ex)
            {
                if (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code)
                    return false;

                throw new DesktopDuplicationException("Failed to acquire next frame.", ex);
            }

            if (desktopResource == null) throw new Exception("desktopResource is null");

            if (_smallerTexture == null)
            {
                _smallerTexture = new Texture2D(_device, new Texture2DDescription
                {
                    CpuAccessFlags = CpuAccessFlags.None,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                    Format = Format.B8G8R8A8_UNorm,
                    Width = desktopWidth,
                    Height = desktopHeight,
                    OptionFlags = ResourceOptionFlags.GenerateMipMaps,
                    MipLevels = mipMapLevel + 1,
                    ArraySize = 1,
                    SampleDescription = { Count = 1, Quality = 0 },
                    Usage = ResourceUsage.Default
                });
                _smallerTextureView = new ShaderResourceView(_device, _smallerTexture);
            }

            using (var tempTexture = desktopResource.QueryInterface<Texture2D>())
            {
                _device.ImmediateContext.CopySubresourceRegion(tempTexture, 0, null, _smallerTexture, 0);
            }

            _outputDuplication.ReleaseFrame();
            _device.ImmediateContext.GenerateMips(_smallerTextureView);
            _device.ImmediateContext.CopySubresourceRegion(_smallerTexture, mipMapLevel, null, _stagingTexture, 0);

            desktopResource.Dispose();
            return true;
        }

        private Bitmap ProcessFrame(Bitmap reusableImage)
        {
            var mapSource = _device.ImmediateContext.MapSubresource(_stagingTexture, 0, MapMode.Read, MapFlags.None);

            var width = _outputDescription.DesktopBounds.GetWidth() / scalingFactor;
            var height = _outputDescription.DesktopBounds.GetHeight() / scalingFactor;

            Bitmap image;
            if (reusableImage != null && reusableImage.Width == width && reusableImage.Height == height)
                image = reusableImage;
            else
                image = new Bitmap(width, height, PixelFormat.Format32bppRgb);

            var boundsRect = new System.Drawing.Rectangle(0, 0, width, height);
            var mapDest = image.LockBits(boundsRect, ImageLockMode.WriteOnly, image.PixelFormat);
            var sourcePtr = mapSource.DataPointer;
            var destPtr = mapDest.Scan0;

            if (mapSource.RowPitch == mapDest.Stride)
            {
                Utilities.CopyMemory(destPtr, sourcePtr, height * mapDest.Stride);
            }
            else
            {
                for (int y = 0; y < height; y++)
                {
                    Utilities.CopyMemory(destPtr, sourcePtr, width * 4);
                    sourcePtr = IntPtr.Add(sourcePtr, mapSource.RowPitch);
                    destPtr = IntPtr.Add(destPtr, mapDest.Stride);
                }
            }

            image.UnlockBits(mapDest);
            _device.ImmediateContext.UnmapSubresource(_stagingTexture, 0);
            return image;
        }

        public bool IsDisposed { get; private set; }

        public static int ScalingFactor => scalingFactor;

        public void Dispose()
        {
            IsDisposed = true;
            _smallerTexture?.Dispose();
            _smallerTextureView?.Dispose();
            _stagingTexture?.Dispose();
            _outputDuplication?.Dispose();
            _device?.Dispose();
            GC.Collect();
        }
    }
}
