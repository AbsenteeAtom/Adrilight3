using System;
using System.Linq;
using NLog;

namespace adrilight.Util
{
    /// <summary>
    /// Sound to Light pipeline. Captures WASAPI loopback audio, runs a 1024-point Hann-windowed
    /// FFT each frame, and maps visible-spectrum colours to the LEDs via a logarithmically-spaced
    /// frequency band model:
    ///
    ///   • The 20 Hz – 10 kHz range is divided into <see cref="NBands"/> logarithmically-spaced
    ///     bands.  Each band's colour is derived from its centre wavelength (700 nm red → 400 nm
    ///     violet) using the Bruton visible-spectrum approximation.
    ///
    ///   • Each LED is randomly assigned to one of the bands.  With 458 LEDs and 32 bands, ~14
    ///     LEDs share each band, so whenever a band has energy a cluster of same-coloured LEDs
    ///     lights up.
    ///
    ///   • Band energy = per-bin-average RMS of the FFT magnitudes within the band's bin range,
    ///     normalised to the single-bin reference (0.25) so Sensitivity behaves the same as
    ///     before regardless of band width.
    ///
    ///   • On a strong bass hit the LED→band assignments are randomly reshuffled (≤ once/second).
    ///
    /// The audio hardware boundary is isolated behind <see cref="IAudioCaptureProvider"/>;
    /// AudioCaptureReader is fully testable without a real audio device.
    /// </summary>
    internal sealed class AudioCaptureReader : ILightingMode
    {
        private static readonly ILogger _log = LogManager.GetCurrentClassLogger();

        internal const int FftLength = 1024; // must be a power of 2
        internal const int NBands    = 32;   // logarithmically-spaced bands, 20 Hz – 10 kHz

        // Precomputed Hann window: w[i] = 0.5 * (1 - cos(2π·i / (N-1)))
        private static readonly float[] HannWindow = Enumerable.Range(0, FftLength)
            .Select(i => 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftLength - 1))))
            .ToArray();

        // ── Band descriptor ──────────────────────────────────────────────────────

        /// <summary>A contiguous range of FFT bins with a pre-computed visible-spectrum colour.</summary>
        internal struct FreqBand
        {
            public int BinLo, BinHi;                    // half-open range [BinLo, BinHi)
            public (float r, float g, float b) Color;   // Bruton wavelength RGB, components 0–1
        }

        // ── Instance fields ──────────────────────────────────────────────────────

        private readonly IUserSettings        _settings;
        private readonly ISpotSet             _spotSet;
        private readonly IAudioCaptureProvider _capture;
        private readonly Random               _rng = new Random();

        // Mono sample accumulation buffer
        private readonly float[] _monoBuffer = new float[FftLength];
        private int _bufferPos;

        // Per-spot state — replaced atomically on each reshuffle
        private int[]                          _spotBins;        // band index for each spot (0-based)
        private (float r, float g, float b)[]  _spotBaseColors;  // colour from assigned band
        private float[]                        _spotSmoothed;    // per-spot smoothed brightness

        // Band definitions cached from the most recent reshuffle
        private FreqBand[] _currentBands;

        // Beat detection state
        private long  _lastReshuffleMs;

        // Reshuffle rate limit is derived from SoundToLightMaxBpm at call time: 60 000 ms / BPM

        private volatile bool _isRunning;

        // ── Exposed for unit tests ───────────────────────────────────────────────
        internal int   ReshuffleCount { get; private set; }
        internal int[] SpotBins       => _spotBins;   // band indices, not raw FFT bins

        public LightingMode ModeId    => LightingMode.SoundToLight;
        public bool         IsRunning => _isRunning;

        public AudioCaptureReader(IUserSettings settings, ISpotSet spotSet, IAudioCaptureProvider capture)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _spotSet  = spotSet  ?? throw new ArgumentNullException(nameof(spotSet));
            _capture  = capture  ?? throw new ArgumentNullException(nameof(capture));
        }

        public void Start()
        {
            if (_isRunning) return;
            _bufferPos       = 0;
            _lastReshuffleMs = 0;
            _isRunning = true;
            _capture.Start(OnAudioData);   // sets SampleRate / Channels
            ShuffleSpotAssignments();      // uses SampleRate
            _log.Info("AudioCaptureReader started (Sound to Light mode).");
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _capture.Stop();
            _log.Info("AudioCaptureReader stopped.");
        }

        // ── Spot assignment ──────────────────────────────────────────────────────

        private void ShuffleSpotAssignments()
        {
            int sr    = _capture.SampleRate > 0 ? _capture.SampleRate : 48000;
            var bands = BuildBands(sr);
            _currentBands = bands;

            var spots = _spotSet.Spots;
            int n     = spots.Length;

            var newBandIdx = new int[n];
            var newColors  = new (float r, float g, float b)[n];

            for (int i = 0; i < n; i++)
            {
                int b        = _rng.Next(0, NBands);
                newBandIdx[i] = b;
                newColors[i]  = bands[b].Color;
            }

            // Replace arrays atomically; local refs captured before the swap stay consistent
            _spotBins       = newBandIdx;
            _spotBaseColors = newColors;
            _spotSmoothed   = new float[n];   // reset per-spot smoothing on reshuffle
            ReshuffleCount++;
        }

        // ── Audio data processing ────────────────────────────────────────────────

        private void OnAudioData(float[] samples, int count)
        {
            if (!_isRunning) return;
            int ch    = Math.Max(1, _capture.Channels);
            // Only mix front-L (ch 0) and front-R (ch 1).  Surround/7.1 devices report many
            // channels, but stereo content (e.g. YouTube) only populates the first two.
            // Averaging all channels dilutes the signal proportionally, making beat detection fail.
            int useCh = Math.Min(ch, 2);
            int i     = 0;
            while (i + ch <= count)
            {
                float mono = 0;
                for (int c = 0; c < useCh; c++) mono += samples[i + c];
                mono /= useCh;

                _monoBuffer[_bufferPos++] = mono;
                i += ch;

                if (_bufferPos >= FftLength)
                {
                    RunFft();
                    _bufferPos = 0;
                }
            }
        }

        private void RunFft()
        {
            // Apply Hann window and compute FFT
            var fftData = new NAudio.Dsp.Complex[FftLength];
            for (int i = 0; i < FftLength; i++)
                fftData[i] = new NAudio.Dsp.Complex { X = _monoBuffer[i] * HannWindow[i], Y = 0f };

            NAudio.Dsp.FastFourierTransform.FFT(true, (int)Math.Log2(FftLength), fftData);

            // ── Beat detection — total bass energy 20–200 Hz ────────────────────
            int sr     = _capture.SampleRate > 0 ? _capture.SampleRate : 48000;
            int bassHi = Math.Max(1, FrequencyToBin(200f, sr));

            float bassEnergy = 0f;
            for (int i = 0; i < bassHi && i < FftLength / 2; i++)
                bassEnergy += fftData[i].X * fftData[i].X + fftData[i].Y * fftData[i].Y;
            float rawBass = MathF.Sqrt(bassEnergy);

            // Simple fixed threshold scaled by sensitivity — reliable with any music style.
            // Dynamic comparison (rawBass vs smoothed average) was previously used but failed
            // because the smoother caught up to rawBass within ~300 ms, after which the
            // threshold was always above rawBass and reshuffles never fired.
            float sensScale  = _settings.SoundToLightSensitivity / 50f;
            float beatThresh = Math.Max(0.005f / sensScale, 0.0005f);
            bool  isBeat     = rawBass > beatThresh;

            int  rateLimitMs = 60000 / Math.Max(1, _settings.SoundToLightMaxBpm);
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (isBeat && (nowMs - _lastReshuffleMs) >= rateLimitMs)
            {
                _lastReshuffleMs = nowMs;
                ShuffleSpotAssignments();
                _log.Debug("Beat detected — reshuffled LED assignments.");
            }

            ApplyToSpots(fftData);
        }

        private void ApplyToSpots(NAudio.Dsp.Complex[] fftData)
        {
            var spots    = _spotSet.Spots;
            var bands    = _currentBands;
            var bandIdx  = _spotBins;
            var colors   = _spotBaseColors;
            var smoothed = _spotSmoothed;

            if (bands == null || bandIdx == null || colors == null || smoothed == null ||
                bandIdx.Length != spots.Length) return;

            byte  sens      = _settings.SoundToLightSensitivity;
            byte  smoothing = _settings.SoundToLightSmoothing;
            float attack    = AttackAlpha(smoothing);
            float decay     = DecayAlpha(smoothing);
            float sensScale = sens / 50f;
            int   limit     = fftData.Length / 2;
            // reference calibrated for a 2-channel mono mix (front-L + front-R only).
            const float reference = 0.04f;

            // Compute per-band energy: per-bin-average RMS, normalised by the single-bin reference.
            // Using per-bin average keeps sensitivity consistent regardless of band width.
            var bandLevels = new float[NBands];
            for (int b = 0; b < NBands; b++)
            {
                float energy   = 0f;
                int   binCount = 0;
                for (int bin = bands[b].BinLo; bin < bands[b].BinHi && bin < limit; bin++)
                {
                    energy += fftData[bin].X * fftData[bin].X + fftData[bin].Y * fftData[bin].Y;
                    binCount++;
                }
                float rms     = binCount > 0 ? MathF.Sqrt(energy / binCount) : 0f;
                bandLevels[b] = Math.Clamp(rms / reference * sensScale, 0f, 1f);
            }

            lock (_spotSet.Lock)
            {
                for (int i = 0; i < spots.Length; i++)
                {
                    float rawLevel = bandLevels[bandIdx[i]];
                    Smooth(ref smoothed[i], rawLevel, attack, decay);

                    var (r, g, bColor) = colors[i];
                    float lv = smoothed[i];
                    spots[i].SetColor(
                        (byte)Math.Clamp(r      * lv * _settings.SoundToLightRedGain   * 255f, 0f, 255f),
                        (byte)Math.Clamp(g      * lv * _settings.SoundToLightGreenGain * 255f, 0f, 255f),
                        (byte)Math.Clamp(bColor * lv * _settings.SoundToLightBlueGain  * 255f, 0f, 255f),
                        false);
                }
                _spotSet.IsDirty = true;
            }
        }

        // ── Pure helper functions (internal static for testability) ──────────────

        /// <summary>
        /// Builds the <see cref="NBands"/> logarithmically-spaced frequency bands for the given
        /// sample rate.  Each band's colour is derived from its geometric-mean frequency.
        /// </summary>
        internal static FreqBand[] BuildBands(int sampleRate)
        {
            var bands = new FreqBand[NBands];
            for (int i = 0; i < NBands; i++)
            {
                float fLo     = BandLowFrequency(i);
                float fHi     = BandLowFrequency(i + 1);
                float fCenter = MathF.Sqrt(fLo * fHi);  // geometric mean

                int binLo = Math.Max(1, FrequencyToBin(fLo, sampleRate));
                int binHi = Math.Min(FrequencyToBin(fHi, sampleRate) + 1, FftLength / 2);
                if (binHi <= binLo) binHi = binLo + 1;

                bands[i] = new FreqBand
                {
                    BinLo = binLo,
                    BinHi = binHi,
                    Color = WavelengthToRgb(FrequencyToWavelength(Math.Clamp(fCenter, 20f, 20000f)))
                };
            }
            return bands;
        }

        /// <summary>Low-frequency edge of band <paramref name="bandIndex"/> in Hz (log scale).</summary>
        internal static float BandLowFrequency(int bandIndex)
        {
            const float fMin = 20f, fMax = 20000f;
            return fMin * MathF.Pow(fMax / fMin, (float)bandIndex / NBands);
        }

        /// <summary>Geometric-mean centre frequency of band <paramref name="bandIndex"/> in Hz.</summary>
        internal static float BandCenterFrequency(int bandIndex)
        {
            float fLo = BandLowFrequency(bandIndex);
            float fHi = BandLowFrequency(bandIndex + 1);
            return MathF.Sqrt(fLo * fHi);
        }

        /// <summary>
        /// Returns the lowest FFT bin index that belongs to band <paramref name="bandIndex"/>
        /// at the given sample rate.  Useful in tests for generating a sine that falls in-band.
        /// </summary>
        internal static int BandBinLo(int bandIndex, int sampleRate)
            => Math.Max(1, FrequencyToBin(BandLowFrequency(bandIndex), sampleRate));

        /// <summary>
        /// Maps a frequency in Hz to a visible-light wavelength in nm using a logarithmic scale.
        /// 20 Hz → 700 nm (red), 10 000 Hz → 400 nm (violet).
        /// </summary>
        internal static float FrequencyToWavelength(float hz)
        {
            const float fMin  = 20f,  fMax  = 20000f;
            const float nmMax = 700f, nmMin = 400f;
            float t = MathF.Log(Math.Clamp(hz, fMin, fMax) / fMin) / MathF.Log(fMax / fMin);
            return nmMax - t * (nmMax - nmMin);
        }

        /// <summary>
        /// Converts a visible-light wavelength in nm (400–700) to linear sRGB (0–1 floats)
        /// using the Bruton visible-spectrum approximation.
        ///   400–440 nm  violet → blue  (r ↓, b=1)
        ///   440–490 nm  blue → cyan   (g ↑, b=1)
        ///   490–510 nm  cyan → green  (b ↓, g=1)
        ///   510–580 nm  green → yellow(r ↑, g=1)
        ///   580–645 nm  yellow → red  (g ↓, r=1)
        ///   645–700 nm  red            (r=1)
        /// </summary>
        internal static (float r, float g, float b) WavelengthToRgb(float nm)
        {
            if (nm < 400f || nm > 700f) return (0f, 0f, 0f);

            float r, g, b;
            if      (nm < 440f) { r = (440f - nm) / 40f;  g = 0f;                b = 1f; }
            else if (nm < 490f) { r = 0f;                  g = (nm - 440f) / 50f; b = 1f; }
            else if (nm < 510f) { r = 0f;                  g = 1f;                b = (510f - nm) / 20f; }
            else if (nm < 580f) { r = (nm - 510f) / 70f;  g = 1f;                b = 0f; }
            else if (nm < 645f) { r = 1f;                  g = (645f - nm) / 65f; b = 0f; }
            else                { r = 1f;                  g = 0f;                b = 0f; }

            return (r, g, b);
        }

        /// <summary>Convert a frequency in Hz to the nearest FFT bin index.</summary>
        internal static int FrequencyToBin(float hz, int sampleRate)
            => sampleRate > 0 ? (int)(hz * FftLength / sampleRate) : 0;

        /// <summary>Exponential smoothing attack coefficient (higher smoothing → slower rise).</summary>
        internal static float AttackAlpha(byte smoothing)
        {
            float t = (101f - smoothing) / 100f;
            return t * 0.95f + 0.04f;
        }

        /// <summary>Exponential smoothing decay coefficient (always slower than attack).</summary>
        internal static float DecayAlpha(byte smoothing)
        {
            float t = (101f - smoothing) / 100f;
            return t * 0.35f + 0.01f;
        }

        private static void Smooth(ref float smoothed, float raw, float attackAlpha, float decayAlpha)
        {
            float alpha = raw > smoothed ? attackAlpha : decayAlpha;
            smoothed = alpha * raw + (1f - alpha) * smoothed;
        }
    }
}
