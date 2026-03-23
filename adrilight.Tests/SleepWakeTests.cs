using adrilight.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace adrilight.Tests
{
    [TestClass]
    public class SleepWakeTests
    {
        private (SleepWakeController controller, Mock<IUserSettings> settings, Mock<IModeManager> modeManager)
            Build(bool awarenessEnabled)
        {
            var settings = new Mock<IUserSettings>();
            settings.SetupGet(s => s.SleepWakeAwarenessEnabled).Returns(awarenessEnabled);
            var modeManager = new Mock<IModeManager>();
            var controller = new SleepWakeController(settings.Object, modeManager.Object);
            return (controller, settings, modeManager);
        }

        [TestMethod]
        public void OnSuspend_WhenEnabled_AddsInhibitor()
        {
            var (controller, _, modeManager) = Build(awarenessEnabled: true);

            controller.OnSuspend();

            modeManager.Verify(m => m.AddInhibitor("sleep"), Times.Once());
        }

        [TestMethod]
        public void OnResume_WhenEnabled_RemovesInhibitor()
        {
            var (controller, _, modeManager) = Build(awarenessEnabled: true);

            controller.OnResume();

            modeManager.Verify(m => m.RemoveInhibitor("sleep"), Times.Once());
        }

        [TestMethod]
        public void OnSuspend_WhenDisabled_DoesNotAddInhibitor()
        {
            var (controller, _, modeManager) = Build(awarenessEnabled: false);

            controller.OnSuspend();

            modeManager.Verify(m => m.AddInhibitor(It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public void OnResume_WhenDisabled_DoesNotRemoveInhibitor()
        {
            var (controller, _, modeManager) = Build(awarenessEnabled: false);

            controller.OnResume();

            modeManager.Verify(m => m.RemoveInhibitor(It.IsAny<string>()), Times.Never());
        }

        [TestMethod]
        public void OnSuspend_ThenResume_WhenEnabled_AddsAndRemovesInhibitor()
        {
            var (controller, _, modeManager) = Build(awarenessEnabled: true);

            controller.OnSuspend();
            controller.OnResume();

            modeManager.Verify(m => m.AddInhibitor("sleep"), Times.Once());
            modeManager.Verify(m => m.RemoveInhibitor("sleep"), Times.Once());
        }
    }
}
