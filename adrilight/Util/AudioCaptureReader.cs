using System;
using System.Linq;
using NLog;

namespace adrilight.Util
{
    /// <summary>
    /// Sound to Light pipeline. Captures WASAPI loopback audio, runs a 1024-point Hann-windowed
    /// FFT each frame, maps three logarithmic frequency bands to LED zones, applies exponential
    /// smoothing with separate attack/decay rates, and drives the spot set with tinted colours:
    ///   Bottom spots (bass  20–200 Hz)   → warm orange-red
    ///   Side spots   (mid  200–2000 Hz)  → neutral white
    ///   Top spots    (treble 2–20 kHz)   → cool blue
    ///
    /// The audio hardware boundary is isolated behind <see cref="IAudioCaptureProvider"/>;
    /// AudioCaptureReader is fully testable without a real audio device.
    /// </summary>
    internal sealed class AudioCaptureReader : ILightingMode
    {
        private static readonly ILogger _log = LogManager.GetCurrentClassLogger();

        internal const int FftLength = 1024; // must be a power of 2

        // Precomputed Hann window: w[i] = 0.5 * (1 - cos(2π·i / (N-1)))
        private static readonly float[] HannWindow = Enumerable.Range(0, FftLength)
            .Select(i => 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftLength - 1))))
            .ToArray();

        private readonly IUserSettings _settings;
        private readonly ISpotSet _spotSet;
        private readonly IAudioCaptureProvider _capture;

        // Mono sample accumulation buffer
        private readonly float[] _monoBuffer = new float[FftLength];
        private int _bufferPos;

        // Per-band smoothed output levels: [0]=bass, [1]=mid, [2]=treble
        private readonly float[] _smoothed = new float[3];

        // Zone classification for each spot, computed on Start()
        private BandZone[] _spotZones;

        private volatile bool _isRunning;

        public LightingMode ModeId => LightingMode.SoundToLight;
        public bool IsRunning => _isRunning;

        public AudioCaptureReader(IUserSettings settings, ISpotSet spotSet, IAudioCaptureProvider capture)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _spotSet  = spotSet  ?? throw new ArgumentNullException(nameof(spotSet));
            _capture  = capture  ?? throw new ArgumentNullException(nameof(capture));
        }

        public void Start()
        {
            if (_isRunning) return;
            _bufferPos = 0;
            Array.Clear(_smoothed, 0, _smoothed.Length);
            _spotZones = ClassifySpotZones();
            _isRunning = true;
            _capture.Start(OnAudioData);
            _log.Info("AudioCaptureReader started (Sound to Light mode).");
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _capture.Stop();
            _log.Info("AudioCaptureReader stopped.");
        }

        // ── Zone classification ──────────────────────────────────────────────────

        private BandZone[] ClassifySpotZones()
        {
            var spots = _spotSet.Spots;
            int h = _spotSet.ExpectedScreenHeight;
            var zones = new BandZone[spots.Length];
            for (int i = 0; i < spots.Length; i++)
                zones[i] = ClassifyZone(spots[i], h);
            return zones;
        }

        /// <summary>
        /// Classifies a spot by screen position.
        /// Bottom 35% of screen → Bass; top 35% → Treble; middle 30% → Mid (sides).
        /// </summary>
        internal static BandZone ClassifyZone(ISpot spot, int screenHeight)
        {
            if (screenHeight <= 0) return BandZone.Mid;
            int cy = (spot.Rectangle.Top + spot.Rectangle.Bottom) / 2;
            if (cy > screenHeight * 0.65f) return BandZone.Bass;
            if (cy < screenHeight * 0.35f) return BandZone.Treble;
            return BandZone.Mid;
        }

        // ── Audio data processing ────────────────────────────────────────────────

        private void OnAudioData(float[] samples, int count)
        {
            if (!_isRunning) return;
            int ch = Math.Max(1, _capture.Channels);
            int i = 0;
            while (i + ch <= count)
            {
                // Mix channels to mono
                float mono = 0;
                for (int c = 0; c < ch; c++) mono += samples[i + c];
                mono /= ch;

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
            // Build windowed complex input
            var fftData = new NAudio.Dsp.Complex[FftLength];
            for (int i = 0; i < FftLength; i++)
                fftData[i] = new NAudio.Dsp.Complex { X = _monoBuffer[i] * HannWindow[i], Y = 0f };

            NAudio.Dsp.FastFourierTransform.FFT(true, (int)Math.Log2(FftLength), fftData);

            int sr = _capture.SampleRate;
            int bassBinLo   = FrequencyToBin(20f, sr);
            int bassBinHi   = FrequencyToBin(200f, sr);
            int midBinHi    = FrequencyToBin(2000f, sr);
            int trebleBinHi = Math.Min(FrequencyToBin(20000f, sr), FftLength / 2);

            byte sens = _settings.SoundToLightSensitivity;
            float rawBass   = ComputeBandLevel(fftData, bassBinLo, bassBinHi,   sens);
            float rawMid    = ComputeBandLevel(fftData, bassBinHi, midBinHi,    sens);
            float rawTreble = ComputeBandLevel(fftData, midBinHi,  trebleBinHi, sens);

            byte smoothing = _settings.SoundToLightSmoothing;
            float attack = AttackAlpha(smoothing);
            float decay  = DecayAlpha(smoothing);
            Smooth(ref _smoothed[0], rawBass,   attack, decay);
            Smooth(ref _smoothed[1], rawMid,    attack, decay);
            Smooth(ref _smoothed[2], rawTreble, attack, decay);

            ApplyToSpots(_smoothed[0], _smoothed[1], _smoothed[2]);
        }

        private void ApplyToSpots(float bass, float mid, float treble)
        {
            var spots = _spotSet.Spots;
            var zones = _spotZones;
            if (zones == null || zones.Length != spots.Length) return;

            lock (_spotSet.Lock)
            {
                for (int i = 0; i < spots.Length; i++)
                {
                    float level = zones[i] switch
                    {
                        BandZone.Bass   => bass,
                        BandZone.Mid    => mid,
                        BandZone.Treble => treble,
                        _               => mid
                    };
                    var (r, g, b) = TintColor(zones[i], level);
                    spots[i].SetColor(r, g, b, false);
                }
                _spotSet.IsDirty = true;
            }
        }

        // ── Pure helper functions (internal static for testability) ──────────────

        /// <summary>
        /// Returns the tinted RGB colour for a band zone at the given normalised level [0,1].
        /// Bass: warm orange-red. Treble: cool blue. Mid: neutral white.
        /// </summary>
        internal static (byte r, byte g, byte b) TintColor(BandZone zone, float level)
        {
            level = Math.Clamp(level, 0f, 1f);
            return zone switch
            {
                BandZone.Bass   => ((byte)(255 * level), (byte)(60  * level), (byte)(0)),
                BandZone.Treble => ((byte)(60  * level), (byte)(120 * level), (byte)(255 * level)),
                _               => ((byte)(255 * level), (byte)(255 * level), (byte)(255 * level))
            };
        }

        /// <summary>
        /// Computes normalised band energy from an FFT result in the range [lo, hi) bins.
        /// Uses total band RMS so equal-amplitude pure sines produce equal output regardless
        /// of where they fall in the spectrum.
        ///
        /// NAudio's <see cref="NAudio.Dsp.FastFourierTransform.FFT"/> divides all bins by N
        /// (forward-normalised DFT). For a full-amplitude Hann-windowed sine the peak bin
        /// magnitude is A·(N/2)·0.5 / N = A/4, so the reference is 0.25.
        /// At sensitivity=50 a full-amplitude sine returns ~1.0.
        /// </summary>
        internal static float ComputeBandLevel(NAudio.Dsp.Complex[] fft, int lo, int hi, byte sensitivity)
        {
            float energy = 0f;
            int limit = fft.Length / 2;
            for (int i = lo; i < hi && i < limit; i++)
                energy += fft[i].X * fft[i].X + fft[i].Y * fft[i].Y;

            float rms = MathF.Sqrt(energy);
            // NAudio divides FFT output by N; peak bin magnitude for a full-amplitude
            // Hann-windowed sine = 1/4.  Reference = 0.25 so full amplitude → level ~1.0.
            const float reference = 0.25f;
            float sens = sensitivity / 50f;   // 1.0 at sensitivity=50
            return Math.Clamp(rms / reference * sens, 0f, 1f);
        }

        /// <summary>Convert a frequency in Hz to the nearest FFT bin index.</summary>
        internal static int FrequencyToBin(float hz, int sampleRate)
            => sampleRate > 0 ? (int)(hz * FftLength / sampleRate) : 0;

        /// <summary>
        /// Exponential smoothing attack coefficient for the given smoothing setting.
        /// Higher smoothing → lower alpha (slower rise).
        /// Range: ~0.99 (smoothing=1, instant) to ~0.05 (smoothing=100, very slow).
        /// </summary>
        internal static float AttackAlpha(byte smoothing)
        {
            float t = (101f - smoothing) / 100f;  // 1.0 at smoothing=1, 0.01 at smoothing=100
            return t * 0.95f + 0.04f;
        }

        /// <summary>
        /// Exponential smoothing decay coefficient. Always slower than attack so LEDs
        /// react quickly to transients but fade naturally.
        /// Range: ~0.36 (smoothing=1) to ~0.014 (smoothing=100).
        /// </summary>
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

        // ── Enums ────────────────────────────────────────────────────────────────

        /// <summary>Frequency band mapped to an LED zone.</summary>
        internal enum BandZone { Bass = 0, Mid = 1, Treble = 2 }
    }
}
