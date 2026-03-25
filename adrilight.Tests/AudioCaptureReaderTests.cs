using System;
using System.Drawing;
using adrilight;
using adrilight.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static adrilight.Util.AudioCaptureReader;

namespace adrilight.Tests
{
    /// <summary>
    /// Tests for AudioCaptureReader — physics-inspired colour-frequency band model.
    ///
    /// Design:
    ///   The 20 Hz – 20 kHz range is divided into NBands (32) logarithmically-spaced bands.
    ///   Each LED is randomly assigned to a band; the band's centre frequency is mapped
    ///   logarithmically to a visible wavelength (700 nm red → 400 nm violet) and converted
    ///   to an RGB colour via the Bruton approximation.  Brightness tracks the per-bin-average
    ///   RMS energy of the FFT bins within the assigned band (exponentially smoothed).
    ///   Strong bass hits trigger a random reshuffle of all assignments (max once per second).
    ///
    /// Audio hardware is mocked via IAudioCaptureProvider.
    /// Pure helper functions are tested directly (internal static).
    /// </summary>
    [TestClass]
    public class AudioCaptureReaderTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        private static Mock<IUserSettings> MakeSettings(byte sensitivity = 50, byte smoothing = 1,
            float redGain = 0.6f, float greenGain = 0.85f, float blueGain = 1.0f, int maxBpm = 120,
            bool autoBpm = true)
        {
            var m = new Mock<IUserSettings>();
            m.SetupGet(s => s.SoundToLightSensitivity).Returns(sensitivity);
            m.SetupGet(s => s.SoundToLightSmoothing).Returns(smoothing);
            m.SetupGet(s => s.SoundToLightMaxBpm).Returns(maxBpm);
            m.SetupGet(s => s.SoundToLightAutoBpm).Returns(autoBpm);
            m.SetupGet(s => s.SoundToLightRedGain).Returns(redGain);
            m.SetupGet(s => s.SoundToLightGreenGain).Returns(greenGain);
            m.SetupGet(s => s.SoundToLightBlueGain).Returns(blueGain);
            return m;
        }

        private static (Mock<IAudioCaptureProvider> capture, Func<Action<float[], int>> getCallback) MakeCapture(
            int sampleRate = 44100, int channels = 2)
        {
            var capture = new Mock<IAudioCaptureProvider>();
            capture.SetupGet(c => c.SampleRate).Returns(sampleRate);
            capture.SetupGet(c => c.Channels).Returns(channels);
            Action<float[], int> cb = null;
            capture.Setup(c => c.Start(It.IsAny<Action<float[], int>>()))
                   .Callback<Action<float[], int>>(x => cb = x);
            return (capture, () => cb);
        }

        /// <summary>
        /// Creates a spot whose SetColor calls are recorded into a mutable tuple.
        /// </summary>
        private static (Mock<ISpot> spot, Func<(byte r, byte g, byte b)> color) MakeSpot(int cx, int cy)
        {
            var spot = new Mock<ISpot>();
            spot.SetupGet(s => s.Rectangle).Returns(new Rectangle(cx - 25, cy - 25, 50, 50));
            byte lastR = 0, lastG = 0, lastB = 0;
            spot.Setup(s => s.SetColor(It.IsAny<byte>(), It.IsAny<byte>(), It.IsAny<byte>(), It.IsAny<bool>()))
                .Callback<byte, byte, byte, bool>((r, g, b, _) => { lastR = r; lastG = g; lastB = b; });
            return (spot, () => (lastR, lastG, lastB));
        }

        private static (Mock<ISpotSet> spotSet,
                        Func<(byte r, byte g, byte b)> getColor0,
                        Func<(byte r, byte g, byte b)> getColor1)
            MakeSpotSet()
        {
            var (spot0, getColor0) = MakeSpot(200, 300);
            var (spot1, getColor1) = MakeSpot(600, 700);

            var ss = new Mock<ISpotSet>();
            ss.SetupGet(s => s.ExpectedScreenWidth).Returns(1000);
            ss.SetupGet(s => s.ExpectedScreenHeight).Returns(1000);
            ss.SetupGet(s => s.Lock).Returns(new object());
            ss.SetupProperty(s => s.IsDirty, false);
            ss.SetupGet(s => s.Spots).Returns(new ISpot[] { spot0.Object, spot1.Object });
            return (ss, getColor0, getColor1);
        }

        private static AudioCaptureReader MakeReader(
            Mock<IUserSettings>         settings,
            Mock<ISpotSet>              spotSet,
            Mock<IAudioCaptureProvider> capture)
            => new AudioCaptureReader(settings.Object, spotSet.Object, capture.Object);

        /// <summary>Generates FftLength stereo float samples of a pure sine at the given frequency.</summary>
        private static float[] SineStereo(float freqHz, int sampleRate = 44100, float amplitude = 0.5f)
        {
            var buf = new float[FftLength * 2];
            for (int i = 0; i < FftLength; i++)
            {
                float v = amplitude * MathF.Sin(2f * MathF.PI * freqHz * i / sampleRate);
                buf[i * 2]     = v;
                buf[i * 2 + 1] = v;
            }
            return buf;
        }

        // ── ModeId ────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ModeId_IsSoundToLight()
        {
            var (capture, _) = MakeCapture();
            var (ss, _, _)   = MakeSpotSet();
            Assert.AreEqual(LightingMode.SoundToLight, MakeReader(MakeSettings(), ss, capture).ModeId);
        }

        // ── IsRunning ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void IsRunning_FalseBeforeStart()
        {
            var (capture, _) = MakeCapture();
            var (ss, _, _)   = MakeSpotSet();
            Assert.IsFalse(MakeReader(MakeSettings(), ss, capture).IsRunning);
        }

        [TestMethod]
        public void IsRunning_TrueAfterStart()
        {
            var (capture, _) = MakeCapture();
            var (ss, _, _)   = MakeSpotSet();
            var reader = MakeReader(MakeSettings(), ss, capture);
            reader.Start();
            Assert.IsTrue(reader.IsRunning);
        }

        [TestMethod]
        public void IsRunning_FalseAfterStop()
        {
            var (capture, _) = MakeCapture();
            var (ss, _, _)   = MakeSpotSet();
            var reader = MakeReader(MakeSettings(), ss, capture);
            reader.Start();
            reader.Stop();
            Assert.IsFalse(reader.IsRunning);
        }

        // ── Start / Stop wiring ───────────────────────────────────────────────────

        [TestMethod]
        public void Start_CallsCaptureStart()
        {
            var (capture, _) = MakeCapture();
            var (ss, _, _)   = MakeSpotSet();
            MakeReader(MakeSettings(), ss, capture).Start();
            capture.Verify(c => c.Start(It.IsAny<Action<float[], int>>()), Times.Once);
        }

        [TestMethod]
        public void Stop_CallsCaptureStop()
        {
            var (capture, _) = MakeCapture();
            var (ss, _, _)   = MakeSpotSet();
            var reader = MakeReader(MakeSettings(), ss, capture);
            reader.Start();
            reader.Stop();
            capture.Verify(c => c.Stop(), Times.Once);
        }

        // ── Wavelength helpers ────────────────────────────────────────────────────

        [TestMethod]
        public void WavelengthToRgb_700nm_IsRed()
        {
            var (r, g, b) = WavelengthToRgb(700f);
            Assert.AreEqual(1f, r, "700 nm (red) should have R=1");
            Assert.AreEqual(0f, g, "700 nm (red) should have G=0");
            Assert.AreEqual(0f, b, "700 nm (red) should have B=0");
        }

        [TestMethod]
        public void WavelengthToRgb_450nm_IsBlueDominant()
        {
            var (r, g, b) = WavelengthToRgb(450f);
            Assert.AreEqual(0f, r, "450 nm (blue) should have R=0");
            Assert.AreEqual(1f, b, "450 nm (blue) should have B=1");
            Assert.IsTrue(b > r && b > g, $"Blue channel should dominate at 450 nm (R={r}, G={g}, B={b})");
        }

        [TestMethod]
        public void WavelengthToRgb_530nm_IsGreenDominant()
        {
            var (r, g, b) = WavelengthToRgb(530f);
            Assert.AreEqual(1f, g, "530 nm (green) should have G=1");
            Assert.AreEqual(0f, b, "530 nm (green) should have B=0");
            Assert.IsTrue(g > r, $"Green channel should dominate at 530 nm (R={r}, G={g})");
        }

        [TestMethod]
        public void WavelengthToRgb_400nm_IsViolet()
        {
            // 400 nm: r=(440-400)/40=1, g=0, b=1 → violet (magenta approximation)
            var (r, g, b) = WavelengthToRgb(400f);
            Assert.AreEqual(0f, g, "400 nm (violet) should have G=0");
            Assert.AreEqual(1f, b, "400 nm (violet) should have B=1");
            Assert.AreEqual(1f, r, "400 nm (violet) should have R=1 (Bruton magenta approximation)");
        }

        // ── Frequency-to-wavelength helper ───────────────────────────────────────

        [TestMethod]
        public void FrequencyToWavelength_20Hz_Returns700nm()
        {
            Assert.AreEqual(700f, FrequencyToWavelength(20f), 0.01f, "20 Hz should map to 700 nm (red)");
        }

        [TestMethod]
        public void FrequencyToWavelength_20kHz_Returns400nm()
        {
            Assert.AreEqual(400f, FrequencyToWavelength(20000f), 0.01f, "20 kHz should map to 400 nm (violet)");
        }

        [TestMethod]
        public void FrequencyToWavelength_MonotonicallyDecreasing()
        {
            float prev = FrequencyToWavelength(20f);
            foreach (float hz in new[] { 100f, 500f, 2000f, 8000f, 10000f })
            {
                float nm = FrequencyToWavelength(hz);
                Assert.IsTrue(nm < prev,
                    $"{hz} Hz should map to a lower wavelength than {prev:F1} nm, got {nm:F1} nm");
                prev = nm;
            }
        }

        // ── Band model helpers ────────────────────────────────────────────────────

        [TestMethod]
        public void BuildBands_Returns32Bands()
        {
            var bands = BuildBands(44100);
            Assert.AreEqual(NBands, bands.Length, "BuildBands should return NBands frequency bands.");
        }

        [TestMethod]
        public void BandBinLo_NonDecreasingAcrossBands()
        {
            int prev = BandBinLo(0, 44100);
            for (int b = 1; b < NBands; b++)
            {
                int lo = BandBinLo(b, 44100);
                Assert.IsTrue(lo >= prev,
                    $"BandBinLo should be non-decreasing: band {b} (lo={lo}) < band {b-1} (lo={prev})");
                prev = lo;
            }
        }

        [TestMethod]
        public void LowBand_HasWarmColor()
        {
            // Band 0 centre ≈ 20 Hz → wavelength near 700 nm → red dominant
            var (r, g, b) = WavelengthToRgb(FrequencyToWavelength(BandCenterFrequency(0)));
            Assert.IsTrue(r > b,
                $"Lowest band should produce a warmer (redder) colour (R={r:F2}, B={b:F2})");
        }

        [TestMethod]
        public void HighBand_HasCoolColor()
        {
            // Band 31 centre ≈ 7700 Hz → wavelength near 430 nm → blue dominant
            var (r, g, b) = WavelengthToRgb(FrequencyToWavelength(BandCenterFrequency(NBands - 1)));
            Assert.IsTrue(b > r,
                $"Highest band should produce a cooler (bluer) colour (R={r:F2}, B={b:F2})");
        }

        // ── Audio pipeline ────────────────────────────────────────────────────────

        [TestMethod]
        public void ZeroAudio_SpotsAreSetToBlack()
        {
            var (capture, getCallback) = MakeCapture();
            var (ss, getColor0, _)     = MakeSpotSet();
            var reader = MakeReader(MakeSettings(sensitivity: 50, smoothing: 1), ss, capture);
            reader.Start();
            getCallback()(new float[FftLength * 2], FftLength * 2);

            var (r, g, b) = getColor0();
            Assert.AreEqual(0, r, "Zero audio should produce R=0");
            Assert.AreEqual(0, g, "Zero audio should produce G=0");
            Assert.AreEqual(0, b, "Zero audio should produce B=0");
        }

        [TestMethod]
        public void BurstAtAssignedBand_LightsUpSpot()
        {
            // Feed a sine at the low-edge frequency of the band assigned to spot 0.
            // Because the band covers a range of bins, energy in that bin drives the band level.
            int sampleRate = 44100;
            var (capture, getCallback) = MakeCapture(sampleRate: sampleRate);
            var (ss, getColor0, _)     = MakeSpotSet();
            // smoothing=1 → attackAlpha≈0.99 (near-instant response)
            var reader = MakeReader(MakeSettings(sensitivity: 80, smoothing: 1), ss, capture);
            reader.Start();

            // Priming frame: the new fixed beat threshold is low enough that a 1.0-amplitude
            // bass sine fires a reshuffle on the very first frame.  Feed one frame now to
            // consume that reshuffle; the 1000 ms rate limit then keeps the assignment stable
            // for all subsequent measurement frames.
            {
                int   primeBand = reader.SpotBins[0];
                float primeFreq = BandBinLo(primeBand, sampleRate) * sampleRate / (float)FftLength;
                getCallback()(SineStereo(primeFreq, sampleRate, amplitude: 1.0f), FftLength * 2);
            }

            int   bandIdx = reader.SpotBins[0];                          // stable band index (0-31)
            int   binLo   = BandBinLo(bandIdx, sampleRate);              // lowest FFT bin in band
            float freq    = binLo * sampleRate / (float)FftLength;       // Hz for that bin
            var   burst   = SineStereo(freq, sampleRate, amplitude: 1.0f);

            // Feed enough frames for smoothing to reach a visible level
            for (int i = 0; i < 5; i++)
                getCallback()(burst, burst.Length);

            var (r, g, b) = getColor0();
            Assert.IsTrue(r + g + b > 0,
                $"Spot assigned to band {bandIdx} should be lit after a burst at {freq:F0} Hz (binLo={binLo}).");
        }

        [TestMethod]
        public void HighSensitivity_BrighterThanLowSensitivity()
        {
            int sampleRate = 44100;
            var (captureLow,  getCallbackLow)  = MakeCapture(sampleRate: sampleRate);
            var (capturHigh,  getCallbackHigh) = MakeCapture(sampleRate: sampleRate);
            var (ssLow,  getColor0Low,  _)     = MakeSpotSet();
            var (ssHigh, getColor0High, _)     = MakeSpotSet();

            var readerLow  = MakeReader(MakeSettings(sensitivity:  1, smoothing: 1), ssLow,  captureLow);
            var readerHigh = MakeReader(MakeSettings(sensitivity: 100, smoothing: 1), ssHigh, capturHigh);

            readerLow.Start();
            readerHigh.Start();

            // Priming frames: consume any reshuffle triggered by the low beat threshold so that
            // the assignments read below are stable for the 1000 ms measurement window.
            {
                int   pBandLow  = readerLow.SpotBins[0];
                int   pBandHigh = readerHigh.SpotBins[0];
                float pFreqLow  = BandBinLo(pBandLow,  sampleRate) * sampleRate / (float)FftLength;
                float pFreqHigh = BandBinLo(pBandHigh, sampleRate) * sampleRate / (float)FftLength;
                getCallbackLow()(SineStereo(pFreqLow,  sampleRate, 1.0f), FftLength * 2);
                getCallbackHigh()(SineStereo(pFreqHigh, sampleRate, 1.0f), FftLength * 2);
            }

            // Drive each reader with a sine at the low-edge frequency of its spot-0 band
            int   bandLow    = readerLow.SpotBins[0];
            int   bandHigh   = readerHigh.SpotBins[0];
            float freqLow    = BandBinLo(bandLow,  sampleRate) * sampleRate / (float)FftLength;
            float freqHigh   = BandBinLo(bandHigh, sampleRate) * sampleRate / (float)FftLength;
            var   burstLow   = SineStereo(freqLow,  sampleRate, 1.0f);
            var   burstHigh  = SineStereo(freqHigh, sampleRate, 1.0f);

            for (int i = 0; i < 10; i++)
            {
                getCallbackLow()(burstLow,   burstLow.Length);
                getCallbackHigh()(burstHigh, burstHigh.Length);
            }

            var (rL, gL, bL) = getColor0Low();
            var (rH, gH, bH) = getColor0High();
            int totalLow  = rL + gL + bL;
            int totalHigh = rH + gH + bH;

            Assert.IsTrue(totalHigh > totalLow,
                $"Sensitivity=100 (total={totalHigh}) should be brighter than sensitivity=1 (total={totalLow}).");
        }

        [TestMethod]
        public void AudioData_MarksSpotSetDirty()
        {
            var (capture, getCallback) = MakeCapture();
            var (ss, _, _) = MakeSpotSet();
            var reader = MakeReader(MakeSettings(), ss, capture);
            reader.Start();
            getCallback()(SineStereo(100f), FftLength * 2);
            Assert.IsTrue(ss.Object.IsDirty, "SpotSet.IsDirty should be set after processing audio.");
        }

        // ── Beat detection ────────────────────────────────────────────────────────

        [TestMethod]
        public void Beat_TriggersReshuffle()
        {
            // Bass burst at 60 Hz, amplitude 1.0 → rawBass >> beatThresh → reshuffle fires.
            var (capture, getCallback) = MakeCapture();
            var (ss, _, _) = MakeSpotSet();
            var reader = MakeReader(MakeSettings(sensitivity: 80), ss, capture);
            reader.Start();
            int countAfterStart = reader.ReshuffleCount; // 1 (initial assignment)

            getCallback()(SineStereo(60f, amplitude: 1.0f), FftLength * 2);

            Assert.AreEqual(countAfterStart + 1, reader.ReshuffleCount,
                "A strong bass hit should trigger one additional reshuffle.");
        }

        [TestMethod]
        public void Beat_RateLimited_OncePerSecond()
        {
            var (capture, getCallback) = MakeCapture();
            var (ss, _, _) = MakeSpotSet();
            var reader = MakeReader(MakeSettings(sensitivity: 80), ss, capture);
            reader.Start();

            var bassBurst = SineStereo(60f, amplitude: 1.0f);

            // First burst → beat + reshuffle
            getCallback()(bassBurst, bassBurst.Length);
            int afterFirst = reader.ReshuffleCount;

            // Second burst immediately (<1 ms later) → rate-limited, no second reshuffle
            getCallback()(bassBurst, bassBurst.Length);

            Assert.AreEqual(afterFirst, reader.ReshuffleCount,
                "A second bass hit within 1 second should be rate-limited and NOT trigger a reshuffle.");
        }

        // ── Pure helper functions ─────────────────────────────────────────────────

        [TestMethod]
        public void AttackAlpha_LowSmoothing_IsHigherThanHighSmoothing()
        {
            float fast = AttackAlpha(1);
            float slow = AttackAlpha(100);
            Assert.IsTrue(fast > slow,
                $"Fast attack (smoothing=1, α={fast:F3}) should be > slow attack (smoothing=100, α={slow:F3}).");
        }

        [TestMethod]
        public void DecayAlpha_AlwaysLessThanAttackAlpha()
        {
            for (byte s = 1; s <= 100; s++)
                Assert.IsTrue(DecayAlpha(s) < AttackAlpha(s),
                    $"Decay alpha should always be < attack alpha at smoothing={s}.");
        }

        // ── BPM detection pure helpers ─────────────────────────────────────────

        [TestMethod]
        public void ComputeOnsetStrength_AllIncrease_ReturnsTotalFlux()
        {
            var current  = new float[] { 0.5f, 0.3f, 0.8f };
            var prev     = new float[] { 0.2f, 0.1f, 0.4f };
            float expect = (0.5f - 0.2f) + (0.3f - 0.1f) + (0.8f - 0.4f);
            Assert.AreEqual(expect, AudioCaptureReader.ComputeOnsetStrength(current, prev), 1e-5f);
        }

        [TestMethod]
        public void ComputeOnsetStrength_AllDecrease_ReturnsZero()
        {
            var current = new float[] { 0.1f, 0.2f, 0.3f };
            var prev    = new float[] { 0.5f, 0.6f, 0.7f };
            Assert.AreEqual(0f, AudioCaptureReader.ComputeOnsetStrength(current, prev), 1e-5f);
        }

        [TestMethod]
        public void ComputeOnsetStrength_MixedChanges_ReturnsOnlyPositiveFlux()
        {
            var current = new float[] { 0.5f, 0.1f };
            var prev    = new float[] { 0.2f, 0.3f };
            Assert.AreEqual(0.3f, AudioCaptureReader.ComputeOnsetStrength(current, prev), 1e-5f);
        }

        [TestMethod]
        public void ComputeAutocorrelation_PeriodicSignal_PeaksAtPeriod()
        {
            // Search range [15,30] ensures only the fundamental period (lag=20) is in range,
            // not the second harmonic (lag=40) which has equal autocorrelation for a pure sine.
            int N = 128, period = 20;
            var signal = new float[N];
            for (int i = 0; i < N; i++)
                signal[i] = MathF.Sin(2f * MathF.PI * i / period);

            float[] autocorr = AudioCaptureReader.ComputeAutocorrelation(signal, 15, 30);
            int peakIdx = 0;
            for (int i = 1; i < autocorr.Length; i++)
                if (autocorr[i] > autocorr[peakIdx]) peakIdx = i;
            int peakLag = peakIdx + 15;

            Assert.IsTrue(Math.Abs(peakLag - period) <= 1,
                $"Periodic signal (period={period}) should peak at lag≈{period}, got lag={peakLag}");
        }

        [TestMethod]
        public void ComputeAutocorrelation_FlatSignal_AllNearZero()
        {
            var signal   = new float[128];  // all zeros → mean-subtracted is all zeros
            float[] autocorr = AudioCaptureReader.ComputeAutocorrelation(signal, 5, 40);
            foreach (float v in autocorr)
                Assert.AreEqual(0f, v, 1e-5f, "Zero signal should give zero autocorrelation");
        }

        [TestMethod]
        public void ComputeConfidence_StrongPeak_ReturnsHighConfidence()
        {
            // Large spike at index 10 (lag = 10 + minLag=5 = 15) against a flat background
            var autocorr = new float[30];
            autocorr[10] = 10f;
            float conf = AudioCaptureReader.ComputeConfidence(autocorr, 15, 5);
            Assert.IsTrue(conf > 2f, $"Strong spike should produce confidence > 2, got {conf:F2}");
        }

        [TestMethod]
        public void ComputeConfidence_FlatAutocorr_ReturnsZero()
        {
            var autocorr = new float[30];
            for (int i = 0; i < 30; i++) autocorr[i] = 0.5f;
            float conf = AudioCaptureReader.ComputeConfidence(autocorr, 15, 5);
            Assert.AreEqual(0f, conf, 1e-5f, "Uniform autocorrelation should give confidence=0");
        }

        [TestMethod]
        public void ComputeStability_IdenticalEstimates_ReturnsOne()
        {
            var estimates = new float[] { 120f, 120f, 120f, 120f };
            Assert.AreEqual(1f, AudioCaptureReader.ComputeStability(estimates, 4), 1e-5f);
        }

        [TestMethod]
        public void ComputeStability_WidelyVaryingEstimates_ReturnsZero()
        {
            var estimates = new float[] { 60f, 120f, 180f, 240f };
            Assert.AreEqual(0f, AudioCaptureReader.ComputeStability(estimates, 4));
        }

        [TestMethod]
        public void LagToBpm_RoundTrip_IsConsistent()
        {
            float fps = 44100f / AudioCaptureReader.FftLength;
            int lag = AudioCaptureReader.BpmToLag(120f, fps);
            float bpm = AudioCaptureReader.LagToBpm(lag, fps);
            // Due to rounding, allow ±5 BPM tolerance
            Assert.IsTrue(Math.Abs(bpm - 120f) < 5f,
                $"LagToBpm(BpmToLag(120)) should round-trip to ~120 BPM, got {bpm:F1}");
        }
    }
}
