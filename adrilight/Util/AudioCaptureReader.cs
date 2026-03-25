using CommunityToolkit.Mvvm.ComponentModel;
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
    ///   • The 20 Hz – 20 kHz range is divided into <see cref="NBands"/> logarithmically-spaced
    ///     bands.  Each band's colour is derived from its centre wavelength (700 nm red → 400 nm
    ///     violet) using the Bruton visible-spectrum approximation.
    ///
    ///   • Each LED is randomly assigned to one of the bands.  With 458 LEDs and 32 bands, ~14
    ///     LEDs share each band, so whenever a band has energy a cluster of same-coloured LEDs
    ///     lights up.
    ///
    ///   • Band energy = per-bin-average RMS of the FFT magnitudes within the band's bin range,
    ///     normalised to the single-bin reference (0.04) so Sensitivity behaves the same
    ///     regardless of band width.
    ///
    ///   • On a strong bass hit the LED→band assignments are randomly reshuffled.  The rate limit
    ///     derives from <see cref="IUserSettings.SoundToLightMaxBpm"/> (or the auto-detected BPM
    ///     when <see cref="IUserSettings.SoundToLightAutoBpm"/> is on and confidence is high).
    ///
    /// The audio hardware boundary is isolated behind <see cref="IAudioCaptureProvider"/>;
    /// AudioCaptureReader is fully testable without a real audio device.
    /// </summary>
    internal sealed class AudioCaptureReader : ObservableObject, ILightingMode, IBpmDetector
    {
        private static readonly ILogger _log = LogManager.GetCurrentClassLogger();

        internal const int FftLength = 1024; // must be a power of 2
        internal const int NBands    = 32;   // logarithmically-spaced bands, 20 Hz – 20 kHz

        // ── BPM detection constants ───────────────────────────────────────────────
        internal const int   OnsetBufferSize    = 256;   // ~6 s at 43 fps; circular buffer
        internal const int   AnalysisInterval   = 43;    // run autocorrelation every ~1 second
        internal const int   WarmupFrames       = 86;    // ~2 s before first detection attempt
        internal const float ConfidenceThreshold = 0.50f;// combined score needed for a lock

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

        // ── BPM detection state ───────────────────────────────────────────────────
        private readonly float[] _onsetBuffer = new float[OnsetBufferSize];
        private readonly float[] _prevBandRaw = new float[NBands];  // previous band RMS for flux
        private int   _onsetWritePos;
        private int   _onsetFrameCount;   // frames stored so far, capped at OnsetBufferSize
        private int   _fftFrameCount;     // total FFT frames since Start()
        private readonly float[] _recentBpmEst = new float[4];  // last 4 estimates for stability
        private int   _recentBpmCount;

        // IBpmDetector backing fields
        private int    _detectedBpm;
        private float  _bpmConfidence;
        private string _bpmStatusText = "—";

        private volatile bool _isRunning;

        // ── Exposed for unit tests ───────────────────────────────────────────────
        internal int   ReshuffleCount { get; private set; }
        internal int[] SpotBins       => _spotBins;   // band indices, not raw FFT bins

        public LightingMode ModeId    => LightingMode.SoundToLight;
        public bool         IsRunning => _isRunning;

        // ── IBpmDetector properties ──────────────────────────────────────────────
        public int    DetectedBpm   { get => _detectedBpm;   private set => SetProperty(ref _detectedBpm,   value); }
        public float  BpmConfidence { get => _bpmConfidence; private set => SetProperty(ref _bpmConfidence, value); }
        public string BpmStatusText { get => _bpmStatusText; private set => SetProperty(ref _bpmStatusText, value); }

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

            // Reset BPM detection state
            _onsetWritePos   = 0;
            _onsetFrameCount = 0;
            _fftFrameCount   = 0;
            _recentBpmCount  = 0;
            Array.Clear(_onsetBuffer, 0, OnsetBufferSize);
            Array.Clear(_prevBandRaw, 0, NBands);
            DetectedBpm   = 0;
            BpmConfidence = 0f;
            BpmStatusText = "Warming up\u2026";

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
            BpmStatusText = "\u2014";
            BpmConfidence = 0f;
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

            int sr    = _capture.SampleRate > 0 ? _capture.SampleRate : 48000;
            var bands = _currentBands;
            if (bands == null) return;

            // ── Per-band RMS — shared by colour application and onset strength ───
            float[] bandRms = ComputeBandRms(fftData, bands);

            // ── Onset strength for BPM detection ─────────────────────────────────
            float onset = ComputeOnsetStrength(bandRms, _prevBandRaw);
            Array.Copy(bandRms, _prevBandRaw, NBands);

            _onsetBuffer[_onsetWritePos] = onset;
            _onsetWritePos = (_onsetWritePos + 1) % OnsetBufferSize;
            if (_onsetFrameCount < OnsetBufferSize) _onsetFrameCount++;
            _fftFrameCount++;

            if (_fftFrameCount % AnalysisInterval == 0 && _onsetFrameCount >= WarmupFrames)
                RunBpmDetection(sr);

            // ── Beat detection — total bass energy 20–200 Hz ─────────────────────
            int bassHi = Math.Max(1, FrequencyToBin(200f, sr));

            float bassEnergy = 0f;
            for (int i = 0; i < bassHi && i < FftLength / 2; i++)
                bassEnergy += fftData[i].X * fftData[i].X + fftData[i].Y * fftData[i].Y;
            float rawBass = MathF.Sqrt(bassEnergy);

            // Simple fixed threshold scaled by sensitivity — reliable with any music style.
            float sensScale  = _settings.SoundToLightSensitivity / 50f;
            float beatThresh = Math.Max(0.005f / sensScale, 0.0005f);
            bool  isBeat     = rawBass > beatThresh;

            int  rateLimitMs = 60000 / Math.Max(1, GetEffectiveBpm());
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (isBeat && (nowMs - _lastReshuffleMs) >= rateLimitMs)
            {
                _lastReshuffleMs = nowMs;
                ShuffleSpotAssignments();
                _log.Debug("Beat detected — reshuffled LED assignments.");
            }

            ApplyToSpots(bandRms);
        }

        /// <summary>
        /// Returns the effective reshuffle-rate BPM: the auto-detected tempo when confidence
        /// is sufficient and auto-detect is enabled, otherwise the manual fallback setting.
        /// A hard ceiling of 240 BPM is always applied.
        /// </summary>
        private int GetEffectiveBpm()
        {
            if (_settings.SoundToLightAutoBpm && BpmConfidence >= ConfidenceThreshold && DetectedBpm > 0)
                return Math.Clamp(DetectedBpm, 30, 240);
            return _settings.SoundToLightMaxBpm;
        }

        // ── Per-band RMS computation ─────────────────────────────────────────────

        private static float[] ComputeBandRms(NAudio.Dsp.Complex[] fftData, FreqBand[] bands)
        {
            int limit  = fftData.Length / 2;
            var rms    = new float[NBands];
            for (int b = 0; b < NBands; b++)
            {
                float energy   = 0f;
                int   binCount = 0;
                for (int bin = bands[b].BinLo; bin < bands[b].BinHi && bin < limit; bin++)
                {
                    energy += fftData[bin].X * fftData[bin].X + fftData[bin].Y * fftData[bin].Y;
                    binCount++;
                }
                rms[b] = binCount > 0 ? MathF.Sqrt(energy / binCount) : 0f;
            }
            return rms;
        }

        private void ApplyToSpots(float[] bandRms)
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
            // reference calibrated for a 2-channel mono mix (front-L + front-R only).
            const float reference = 0.04f;

            // Apply sensitivity scaling to the pre-computed RMS values
            var bandLevels = new float[NBands];
            for (int b = 0; b < NBands; b++)
                bandLevels[b] = Math.Clamp(bandRms[b] / reference * sensScale, 0f, 1f);

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

        // ── BPM detection ────────────────────────────────────────────────────────

        private void RunBpmDetection(int sr)
        {
            float fps    = sr > 0 ? sr / (float)FftLength : 44100f / FftLength;
            int   minLag = Math.Max(1, BpmToLag(240f, fps));
            int   maxLag = BpmToLag(30f, fps);

            if (maxLag >= OnsetBufferSize) maxLag = OnsetBufferSize - 1;
            if (minLag >= maxLag) return;

            // Extract chronological signal from circular buffer
            int bufLen   = Math.Min(_onsetFrameCount, OnsetBufferSize);
            int startPos = _onsetFrameCount >= OnsetBufferSize ? _onsetWritePos : 0;
            var signal   = new float[bufLen];
            for (int i = 0; i < bufLen; i++)
                signal[i] = _onsetBuffer[(startPos + i) % OnsetBufferSize];

            if (maxLag >= bufLen) maxLag = bufLen - 1;
            if (minLag >= maxLag) return;

            var autocorr = ComputeAutocorrelation(signal, minLag, maxLag);

            // Find peak lag
            int peakIdx = 0;
            for (int i = 1; i < autocorr.Length; i++)
                if (autocorr[i] > autocorr[peakIdx]) peakIdx = i;
            int peakLag = peakIdx + minLag;

            float rawBpm     = LagToBpm(peakLag, fps);
            float sigmaConf  = ComputeConfidence(autocorr, peakLag, minLag);
            float normConf   = Math.Clamp(sigmaConf / 5f, 0f, 1f);

            // Update stability history
            _recentBpmEst[_recentBpmCount % _recentBpmEst.Length] = rawBpm;
            if (_recentBpmCount < _recentBpmEst.Length) _recentBpmCount++;

            float stability = ComputeStability(_recentBpmEst, _recentBpmCount);
            float combined  = (normConf + stability) * 0.5f;

            int detectedBpm = (int)Math.Clamp(MathF.Round(rawBpm), 30f, 240f);

            BpmConfidence = combined;

            if (_onsetFrameCount < OnsetBufferSize)
            {
                BpmStatusText = "Warming up\u2026";
            }
            else if (combined >= ConfidenceThreshold)
            {
                DetectedBpm   = detectedBpm;
                BpmStatusText = combined >= 0.75f
                    ? $"Detected: {detectedBpm} BPM \u2014 Good lock"
                    : $"Detected: {detectedBpm} BPM \u2014 Weak signal";
            }
            else
            {
                BpmStatusText = "Detecting\u2026";
            }
        }

        // ── Pure helper functions (internal static for testability) ──────────────

        /// <summary>
        /// Spectral flux onset strength: sum of positive band-energy increases across all bands.
        /// Returns a non-negative float; larger values indicate more sudden spectral change.
        /// </summary>
        internal static float ComputeOnsetStrength(float[] currentBands, float[] prevBands)
        {
            float flux = 0f;
            int   len  = Math.Min(currentBands.Length, prevBands.Length);
            for (int i = 0; i < len; i++)
                flux += Math.Max(0f, currentBands[i] - prevBands[i]);
            return flux;
        }

        /// <summary>
        /// Mean-subtracted normalised autocorrelation of <paramref name="signal"/> for lags
        /// in [<paramref name="minLag"/>, <paramref name="maxLag"/>].
        /// Returns a float[] of length (maxLag - minLag + 1), indexed by (lag - minLag).
        /// </summary>
        internal static float[] ComputeAutocorrelation(float[] signal, int minLag, int maxLag)
        {
            int   n      = signal.Length;
            float mean   = 0f;
            for (int i = 0; i < n; i++) mean += signal[i];
            mean /= n;

            int     count  = maxLag - minLag + 1;
            float[] result = new float[count];
            for (int lagIdx = 0; lagIdx < count; lagIdx++)
            {
                int   lag = lagIdx + minLag;
                float sum = 0f;
                int   terms = n - lag;
                if (terms <= 0) continue;
                for (int t = 0; t < terms; t++)
                    sum += (signal[t] - mean) * (signal[t + lag] - mean);
                result[lagIdx] = sum / terms;
            }
            return result;
        }

        /// <summary>
        /// Peak prominence score (σ) for the autocorrelation array at <paramref name="peakLag"/>.
        /// Returns (peakValue − mean) / stdDev, or 0 when stdDev is negligible.
        /// <paramref name="autocorr"/> is indexed by (lag − minLag).
        /// </summary>
        internal static float ComputeConfidence(float[] autocorr, int peakLag, int minLag)
        {
            if (autocorr.Length == 0) return 0f;

            float mean = 0f;
            for (int i = 0; i < autocorr.Length; i++) mean += autocorr[i];
            mean /= autocorr.Length;

            float variance = 0f;
            for (int i = 0; i < autocorr.Length; i++)
            {
                float d = autocorr[i] - mean;
                variance += d * d;
            }
            float std = MathF.Sqrt(variance / autocorr.Length);
            if (std < 1e-10f) return 0f;

            float peakValue = autocorr[peakLag - minLag];
            return Math.Max(0f, (peakValue - mean) / std);
        }

        /// <summary>
        /// Stability score 0..1 for the last <paramref name="count"/> BPM estimates.
        /// Returns 1 when all estimates are identical; 0 when the coefficient of variation
        /// exceeds 25%.
        /// </summary>
        internal static float ComputeStability(float[] estimates, int count)
        {
            if (count < 2) return 0f;

            float mean = 0f;
            for (int i = 0; i < count; i++) mean += estimates[i];
            mean /= count;
            if (mean < 1f) return 0f;

            float sumSq = 0f;
            for (int i = 0; i < count; i++)
            {
                float d = estimates[i] - mean;
                sumSq += d * d;
            }
            float cv = MathF.Sqrt(sumSq / count) / mean;   // coefficient of variation
            return Math.Max(0f, 1f - cv / 0.25f);
        }

        /// <summary>Converts an autocorrelation lag (in FFT frames) to BPM.</summary>
        internal static float LagToBpm(int lag, float framesPerSecond)
            => lag > 0 ? framesPerSecond * 60f / lag : 0f;

        /// <summary>Converts a BPM value to the nearest autocorrelation lag (in FFT frames).</summary>
        internal static int BpmToLag(float bpm, float framesPerSecond)
            => bpm > 0f ? (int)MathF.Round(framesPerSecond * 60f / bpm) : 0;

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
        /// 20 Hz → 700 nm (red), 20 000 Hz → 400 nm (violet).
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
