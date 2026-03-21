using adrilight.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace adrilight.Tests
{
    [TestClass]
    public class SleepWakeTests
    {
        private Mock<IUserSettings> BuildSettings(bool awarenessEnabled, bool transferActive)
        {
            var mock = new Mock<IUserSettings>();
            mock.SetupProperty(s => s.SleepWakeAwarenessEnabled, awarenessEnabled);
            mock.SetupProperty(s => s.TransferActive, transferActive);
            return mock;
        }

        [TestMethod]
        public void OnSuspend_WhenEnabled_TurnsOffTransferAndSavesState()
        {
            var settings = BuildSettings(awarenessEnabled: true, transferActive: true);
            var controller = new SleepWakeController(settings.Object);

            controller.OnSuspend();

            Assert.IsFalse(settings.Object.TransferActive, "TransferActive should be false after suspend");
        }

        [TestMethod]
        public void OnResume_AfterSuspend_RestoresTransferActive()
        {
            var settings = BuildSettings(awarenessEnabled: true, transferActive: true);
            var controller = new SleepWakeController(settings.Object);

            controller.OnSuspend(); // saves true, sets false
            controller.OnResume();  // should restore true

            Assert.IsTrue(settings.Object.TransferActive, "TransferActive should be restored to true after wake");
        }

        [TestMethod]
        public void OnResume_WhenTransferWasAlreadyOff_DoesNotTurnOn()
        {
            var settings = BuildSettings(awarenessEnabled: true, transferActive: false);
            var controller = new SleepWakeController(settings.Object);

            controller.OnSuspend(); // saves false, sets false
            controller.OnResume();  // should not turn on

            Assert.IsFalse(settings.Object.TransferActive, "TransferActive should remain false if it was off before sleep");
        }

        [TestMethod]
        public void OnSuspend_WhenDisabled_DoesNotChangeTransferActive()
        {
            var settings = BuildSettings(awarenessEnabled: false, transferActive: true);
            var controller = new SleepWakeController(settings.Object);

            controller.OnSuspend();

            Assert.IsTrue(settings.Object.TransferActive, "TransferActive should be unchanged when awareness is disabled");
        }

        [TestMethod]
        public void OnResume_WhenDisabled_DoesNotChangeTransferActive()
        {
            var settings = BuildSettings(awarenessEnabled: false, transferActive: false);
            var controller = new SleepWakeController(settings.Object);

            // Simulate a sleep/wake cycle with awareness disabled throughout
            controller.OnSuspend();
            controller.OnResume();

            Assert.IsFalse(settings.Object.TransferActive, "TransferActive should be unchanged when awareness is disabled");
        }
    }
}
