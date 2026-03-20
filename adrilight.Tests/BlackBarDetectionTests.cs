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

        // -----------------------------------------------------------------------
        // GetSamplingRectangle tests
        // -----------------------------------------------------------------------

        [TestMethod]
        public void GetSamplingRectangle_SpotInsideActiveRegion_ReturnsIntersection()
        {
            // Spot fully inside content area — sampling rect equals the spot
            var active = new Rectangle(10, 20, 80, 40);  // x=10..90, y=20..60
            var spot   = new Rectangle(30, 30, 20, 10);  // fully inside

            var result = DesktopDuplicatorReader.GetSamplingRectangle(spot, active);

            Assert.AreEqual(spot, result, "Spot inside active region should sample from itself");
        }

        [TestMethod]
        public void GetSamplingRectangle_SpotAboveActiveRegion_ClampsToTopEdge()
        {
            // Letterbox scenario: spot is entirely above the content area
            var active = new Rectangle(0, 20, 100, 40);  // content rows 20–60
            var spot   = new Rectangle(10, 0, 20, 15);   // above content (rows 0–15)

            var result = DesktopDuplicatorReader.GetSamplingRectangle(spot, active);

            Assert.AreEqual(active.Top, result.Top,    "Should clamp to top edge of content");
            Assert.AreEqual(active.Top + 1, result.Bottom, "Clamped row should be 1 pixel tall");
            // Horizontal: spot overlaps active horizontally, so intersection kept
            Assert.AreEqual(10, result.Left);
            Assert.AreEqual(30, result.Right);
        }

        [TestMethod]
        public void GetSamplingRectangle_SpotBelowActiveRegion_ClampsToBottomEdge()
        {
            // Spot is entirely below the content area
            var active = new Rectangle(0, 20, 100, 40);  // content rows 20–60
            var spot   = new Rectangle(10, 65, 20, 10);  // below content (rows 65–75)

            var result = DesktopDuplicatorReader.GetSamplingRectangle(spot, active);

            Assert.AreEqual(active.Bottom - 1, result.Top, "Should clamp to bottom edge of content");
            Assert.AreEqual(active.Bottom,     result.Bottom);
        }

        [TestMethod]
        public void GetSamplingRectangle_SpotLeftOfActiveRegion_ClampsToLeftEdge()
        {
            // Pillarbox scenario: spot is entirely to the left of content
            var active = new Rectangle(20, 0, 60, 80);   // content cols 20–80
            var spot   = new Rectangle(0, 10, 15, 20);   // left of content (cols 0–15)

            var result = DesktopDuplicatorReader.GetSamplingRectangle(spot, active);

            Assert.AreEqual(active.Left,     result.Left,  "Should clamp to left edge of content");
            Assert.AreEqual(active.Left + 1, result.Right, "Clamped column should be 1 pixel wide");
        }

        [TestMethod]
        public void GetSamplingRectangle_SpotRightOfActiveRegion_ClampsToRightEdge()
        {
            // Spot entirely to the right of content
            var active = new Rectangle(20, 0, 60, 80);   // content cols 20–80
            var spot   = new Rectangle(85, 10, 15, 20);  // right of content (cols 85–100)

            var result = DesktopDuplicatorReader.GetSamplingRectangle(spot, active);

            Assert.AreEqual(active.Right - 1, result.Left,  "Should clamp to right edge of content");
            Assert.AreEqual(active.Right,     result.Right);
        }

        [TestMethod]
        public void GetSamplingRectangle_EmptyActiveRegion_ReturnsSpotUnchanged()
        {
            // When active region is empty (full-black frame) sampling falls back to spot itself
            var spot = new Rectangle(10, 10, 20, 20);

            var result = DesktopDuplicatorReader.GetSamplingRectangle(spot, Rectangle.Empty);

            Assert.AreEqual(spot, result, "Empty active region should return spot rectangle unchanged");
        }

        [TestMethod]
        public void GetSamplingRectangle_SpotPartiallyOverlapsActiveRegion_ReturnsIntersection()
        {
            // Spot straddles the top bar boundary — overlapping part is sampled
            var active = new Rectangle(0, 20, 100, 40);  // content rows 20–60
            var spot   = new Rectangle(10, 15, 30, 20);  // rows 15–35, partially in content

            var result = DesktopDuplicatorReader.GetSamplingRectangle(spot, active);

            Assert.AreEqual(20, result.Top,    "Top of result should be content top (intersection)");
            Assert.AreEqual(35, result.Bottom, "Bottom of result should be spot bottom (inside content)");
        }
    }
}
