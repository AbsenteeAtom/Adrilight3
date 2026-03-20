using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace adrilight.Tests
{
    [TestClass]
    public class BlackBarDetectionTests
    {
        private const byte Threshold = 20;

        // Creates a 32bppArgb bitmap, fills it black, then paints a white rectangle
        // over the content rows/columns. The bitmap is locked and passed to DetectBlackBars.
        private static BitmapData LockBitmap(Bitmap bmp)
        {
            var data = new BitmapData();
            bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppRgb,
                data);
            return data;
        }

        [TestMethod]
        public void DetectBlackBars_FullyBlackFrame_ReturnsEmpty()
        {
            // Arrange — a 100×80 bitmap filled entirely with black
            using var bmp = new Bitmap(100, 80);
            using (var g = Graphics.FromImage(bmp))
                g.Clear(Color.Black);

            var data = LockBitmap(bmp);
            try
            {
                // Act
                var result = DesktopDuplicatorReader.DetectBlackBars(data, bmp.Width, bmp.Height, Threshold);

                // Assert
                Assert.AreEqual(Rectangle.Empty, result, "Fully black frame should return Rectangle.Empty");
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        [TestMethod]
        public void DetectBlackBars_LetterboxedFrame_CropsTopAndBottomBars()
        {
            // Arrange — 100×80 bitmap with 20% black bars top and bottom, white content in middle
            // Black: rows 0–15 and 64–79 (16 rows each = 20%)
            // White: rows 16–63 (48 rows = 60%)
            const int width = 100;
            const int height = 80;
            const int contentTop = 16;
            const int contentBottom = 64; // exclusive

            using var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                g.FillRectangle(Brushes.White, 0, contentTop, width, contentBottom - contentTop);
            }

            var data = LockBitmap(bmp);
            try
            {
                // Act
                var result = DesktopDuplicatorReader.DetectBlackBars(data, width, height, Threshold);

                // Assert — active region should exclude the black bars
                Assert.AreNotEqual(Rectangle.Empty, result, "Should detect active content region");
                Assert.AreEqual(contentTop,    result.Top,    "cropTop should be at first content row");
                Assert.AreEqual(contentBottom, result.Bottom, "cropBottom should be just past last content row");
                Assert.AreEqual(0,             result.Left,   "No left bar — cropLeft should be 0");
                Assert.AreEqual(width,         result.Right,  "No right bar — cropRight should be full width");
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        [TestMethod]
        public void DetectBlackBars_PillarboxedFrame_CropsLeftAndRightBars()
        {
            // Arrange — 100×80 bitmap with 20% black bars left and right, white content in centre
            // Black: cols 0–19 and 80–99
            // White: cols 20–79
            const int width = 100;
            const int height = 80;
            const int contentLeft = 20;
            const int contentRight = 80; // exclusive

            using var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                g.FillRectangle(Brushes.White, contentLeft, 0, contentRight - contentLeft, height);
            }

            var data = LockBitmap(bmp);
            try
            {
                var result = DesktopDuplicatorReader.DetectBlackBars(data, width, height, Threshold);

                Assert.AreNotEqual(Rectangle.Empty, result, "Should detect active content region");
                Assert.AreEqual(0,            result.Top,    "No top bar — cropTop should be 0");
                Assert.AreEqual(height,       result.Bottom, "No bottom bar — cropBottom should be full height");
                Assert.AreEqual(contentLeft,  result.Left,   "cropLeft should be at first content column");
                Assert.AreEqual(contentRight, result.Right,  "cropRight should be just past last content column");
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        [TestMethod]
        public void DetectBlackBars_FullyBrightFrame_ReturnsFullFrameRectangle()
        {
            // Arrange — a 100×80 bitmap filled entirely with white (no bars at all)
            const int width = 100;
            const int height = 80;
            using var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
                g.Clear(Color.White);

            var data = LockBitmap(bmp);
            try
            {
                var result = DesktopDuplicatorReader.DetectBlackBars(data, width, height, Threshold);

                Assert.AreEqual(new Rectangle(0, 0, width, height), result,
                    "No bars — result should be the full frame rectangle");
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }
    }
}
