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
    /// Tests for AudioCaptureReader.
    /// The audio hardware boundary is mocked via IAudioCaptureProvider.
    /// Spot I/O is mocked via ISpotSet + ISpot.
    /// All pure helper functions are tested directly (they are internal static).
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
        /// The spot's rectangle centroid is at (cx, cy) with a 50×50 area.
        /// </summary>
        private static (Mock<ISpot> spot, Func<(byte r, byte g, byte b)> color) MakeSpot(int cx, int cy)
        {
            var spot = new Mock<ISpot>();
            spot.SetupGet(s => s.Rectangle)
                .Returns(new Rectangle(cx - 25, cy - 25, 50, 50));
            byte lastR = 0, lastG = 0, lastB = 0;
            spot.Setup(s => s.SetColor(It.IsAny<byte>(), It.IsAny<byte>(), It.IsAny<byte>(), It.IsAny<bool>()))
                .Callback<byte, byte, byte, bool>((r, g, b, _) => { lastR = r; lastG = g; lastB = b; });
            return (spot, () => (lastR, lastG, lastB));
        }

        /// <summary>
        /// Creates a spot set with one bottom spot (cy=870) and one top spot (cy=130)
        /// on a 1000×1000 virtual screen.
        /// </summary>
        private static (Mock<ISpotSet> spotSet,
                        Func<(byte r, byte g, byte b)> bottomColor,
                        Func<(byte r, byte g, byte b)> topColor)
            MakeSpotSet()
        {
            var (bottomSpot, getBottomColor) = MakeSpot(500, 870);  // bottom 35% → Bass
            var (topSpot,    getTopColor)    = MakeSpot(500, 130);   // top 35%    → Treble

            var ss = new Mock<ISpotSet>();
            ss.SetupGet(s => s.ExpectedScreenWidth).Returns(1000);
            ss.SetupGet(s => s.ExpectedScreenHeight).Returns(1000);
            ss.SetupGet(s => s.Lock).Returns(new object());
            ss.SetupProperty(s => s.IsDirty, false);
            ss.SetupGet(s => s.Spots).Returns(new ISpot[] { bottomSpot.Object, topSpot.Object });
            return (ss, getBottomColor, getTopColor);
        }

        private static AudioCaptureReader MakeReader(
            Mock<IUserSettings> settings,
            Mock<ISpotSet> spotSet,
            Mock<IAudioCaptureProvider> capture)
            => new AudioCaptureReader(settings.Object, spotSet.Object, capture.Object);

        /// <summary>Generates 1024 stereo float samples of a pure sine at the given frequency.</summary>
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
            var reader = MakeReader(MakeSettings(), ss, capture);
            Assert.AreEqual(LightingMode.SoundToLight, reader.ModeId);
        }

        // ── IsRunning ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void IsRunning_FalseBeforeStart()
        {
            var (capture, _) = MakeCapture();
            var (ss, _, _)   = MakeSpotSet();
            var reader = MakeReader(MakeSettings(), ss, capture);
            Assert.IsFalse(reader.IsRunning);
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
            var reader = MakeReader(MakeSettings(), ss, capture);
            reader.Start();
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

        // ── Audio pipeline ────────────────────────────────────────────────────────

        [TestMethod]
        public void ZeroAudio_SpotsAreSetToBlack()
        {
            var (capture, getCallback) = MakeCapture();
            var (ss, getBottomColor, getTopColor) = MakeSpotSet();
            var reader = MakeReader(MakeSettings(sensitivity: 50, smoothing: 1), ss, capture);

            reader.Start();
            // Inject one full FFT window of silence
            getCallback()(new float[FftLength * 2], FftLength * 2);

            var (r, g, b) = getBottomColor();
            Assert.AreEqual(0, r, "Zero audio should produce no bottom colour");
            Assert.AreEqual(0, g);
            Assert.AreEqual(0, b);
        }

        [TestMethod]
        public void BassBurst_BottomSpotsGetWarmColor()
        {
            var (capture, getCallback) = MakeCapture();
            var (ss, getBottomColor, getTopColor) = MakeSpotSet();
            // smoothing=1 → attackAlpha≈0.99, nearly instant response
            var reader = MakeReader(MakeSettings(sensitivity: 50, smoothing: 1), ss, capture);

            reader.Start();
            // 100 Hz sine → falls in bass band (bins 0–4 at 44100 Hz)
            getCallback()(SineStereo(100f), FftLength * 2);

            var (r, g, b) = getBottomColor();
            Assert.IsTrue(r > 0, $"Bottom (bass) spot R should be > 0 after bass burst, was {r}");
            Assert.IsTrue(r > b, $"Bottom spot should be warmer (R={r} > B={b})");
        }

        [TestMethod]
        public void TrebleBurst_TopSpotsGetCoolColor()
        {
            var (capture, getCallback) = MakeCapture();
            var (ss, getBottomColor, getTopColor) = MakeSpotSet();
            var reader = MakeReader(MakeSettings(sensitivity: 50, smoothing: 1), ss, capture);

            reader.Start();
            // 5000 Hz sine → treble band (bin ~116 at 44100 Hz)
            getCallback()(SineStereo(5000f), FftLength * 2);

            var (r, g, b) = getTopColor();
            Assert.IsTrue(b > 0, $"Top (treble) spot B should be > 0 after treble burst, was {b}");
            Assert.IsTrue(b > r, $"Top spot should be cooler (B={b} > R={r})");
        }

        [TestMethod]
        public void HighSensitivity_BrighterThanLowSensitivity()
        {
            // Run two readers with the same bass burst; compare bottom spot brightness.
            var (captureLow, getCallbackLow) = MakeCapture();
            var (ssLow, getLow, _) = MakeSpotSet();
            var readerLow = MakeReader(MakeSettings(sensitivity: 1, smoothing: 1), ssLow, captureLow);

            var (captureHigh, getCallbackHigh) = MakeCapture();
            var (ssHigh, getHigh, _) = MakeSpotSet();
            var readerHigh = MakeReader(MakeSettings(sensitivity: 100, smoothing: 1), ssHigh, captureHigh);

            float[] samples = SineStereo(100f);
            readerLow.Start();
            getCallbackLow()(samples, samples.Length);
            var (rLow, _, _) = getLow();

            readerHigh.Start();
            getCallbackHigh()(samples, samples.Length);
            var (rHigh, _, _) = getHigh();

            Assert.IsTrue(rHigh > rLow,
                $"Sensitivity=100 (R={rHigh}) should be brighter than sensitivity=1 (R={rLow})");
        }

        [TestMethod]
        public void AudioData_MarksSpotSetDirty()
        {
            var (capture, getCallback) = MakeCapture();
            var (ss, _, _) = MakeSpotSet();
            var reader = MakeReader(MakeSettings(), ss, capture);

            reader.Start();
            getCallback()(SineStereo(100f), FftLength * 2);

            Assert.IsTrue(ss.Object.IsDirty, "SpotSet.IsDirty should be set after processing audio");
        }

        // ── Pure function tests (no mocking needed) ───────────────────────────────

        [TestMethod]
        public void TintColor_Bass_IsWarmOrangeRed()
        {
            var (r, g, b) = TintColor(BandZone.Bass, 1.0f);
            Assert.AreEqual(255, r, "Bass full-brightness R");
            Assert.AreEqual(60,  g, "Bass full-brightness G");
            Assert.AreEqual(0,   b, "Bass full-brightness B");
            Assert.IsTrue(r > b, "Bass should be warmer than cool");
        }

        [TestMethod]
        public void TintColor_Treble_IsCoolBlue()
        {
            var (r, g, b) = TintColor(BandZone.Treble, 1.0f);
            Assert.AreEqual(60,  r, "Treble full-brightness R");
            Assert.AreEqual(120, g, "Treble full-brightness G");
            Assert.AreEqual(255, b, "Treble full-brightness B");
            Assert.IsTrue(b > r, "Treble should be cooler than warm");
        }

        [TestMethod]
        public void TintColor_Mid_IsNeutralWhite()
        {
            var (r, g, b) = TintColor(BandZone.Mid, 1.0f);
            Assert.AreEqual(255, r);
            Assert.AreEqual(255, g);
            Assert.AreEqual(255, b);
        }

        [TestMethod]
        public void TintColor_ZeroLevel_IsBlack()
        {
            var (r, g, b) = TintColor(BandZone.Bass, 0f);
            Assert.AreEqual(0, r);
            Assert.AreEqual(0, g);
            Assert.AreEqual(0, b);
        }

        [TestMethod]
        public void ClassifyZone_BottomOfScreen_IsBass()
        {
            var spot = new Mock<ISpot>();
            spot.SetupGet(s => s.Rectangle).Returns(new Rectangle(450, 820, 100, 100));  // cy=870
            Assert.AreEqual(BandZone.Bass, ClassifyZone(spot.Object, 1000));
        }

        [TestMethod]
        public void ClassifyZone_TopOfScreen_IsTreble()
        {
            var spot = new Mock<ISpot>();
            spot.SetupGet(s => s.Rectangle).Returns(new Rectangle(450, 80, 100, 100));  // cy=130
            Assert.AreEqual(BandZone.Treble, ClassifyZone(spot.Object, 1000));
        }

        [TestMethod]
        public void ClassifyZone_MiddleOfScreen_IsMid()
        {
            var spot = new Mock<ISpot>();
            spot.SetupGet(s => s.Rectangle).Returns(new Rectangle(0, 450, 50, 100));  // cy=500
            Assert.AreEqual(BandZone.Mid, ClassifyZone(spot.Object, 1000));
        }

        [TestMethod]
        public void AttackAlpha_LowSmoothing_IsHigherThanHighSmoothing()
        {
            float fast = AttackAlpha(1);
            float slow = AttackAlpha(100);
            Assert.IsTrue(fast > slow,
                $"Fast attack (smoothing=1, α={fast:F3}) should be > slow (smoothing=100, α={slow:F3})");
        }

        [TestMethod]
        public void DecayAlpha_AlwaysLessThanAttackAlpha()
        {
            for (byte s = 1; s <= 100; s++)
                Assert.IsTrue(DecayAlpha(s) < AttackAlpha(s),
                    $"Decay alpha should always be < attack alpha at smoothing={s}");
        }
    }
}
