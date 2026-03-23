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
    ///   The 20 Hz – 10 kHz range is divided into NBands (32) logarithmically-spaced bands.
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

        private static Mock<IUserSettings> MakeSettings(byte sensitivity = 50, byte smoothing = 1)
        {
            var m = new Mock<IUserSettings>();
            m.SetupGet(s => s.SoundToLightSensitivity).Returns(sensitivity);
            m.SetupGet(s => s.SoundToLightSmoothing).Returns(smoothing);
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
        public void FrequencyToWavelength_10kHz_Returns400nm()
        {
            Assert.AreEqual(400f, FrequencyToWavelength(10000f), 0.01f, "10 kHz should map to 400 nm (violet)");
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

            int   bandIdx = reader.SpotBins[0];                          // band index (0-31)
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
            // Bass burst with _bassSmoothed=0 at start → rawBass > max(0,0.05)=0.05 → beat fires.
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
    }
}
