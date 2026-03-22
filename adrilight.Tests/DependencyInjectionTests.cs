using System;
using System.IO;
using adrilight.ViewModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ninject;

namespace adrilight.Tests
{
    [TestClass]
    public class DependencyInjectionTests
    {
        [TestMethod]
        public void DesignTimeCreation_Works()
        {
            var kernel = App.SetupDependencyInjection(true);

            var UserSettings = kernel.Get<IUserSettings>();
            Assert.IsNotNull(UserSettings, "UserSettings");

            var settingsViewModel = kernel.Get<SettingsViewModel>();
            Assert.IsNotNull(settingsViewModel, "settingsViewModel");
        }

        [TestMethod]
        public void RunTimeCreation_Works()
        {
            var tempFolder = Path.Combine(Path.GetTempPath(), "adrilight-tests-" + Guid.NewGuid());
            var kernel = App.SetupDependencyInjection(false, settingsFolder: tempFolder);

            var UserSettings = kernel.Get<IUserSettings>();
            Assert.IsNotNull(UserSettings, "UserSettings");

            var settingsViewModel = kernel.Get<SettingsViewModel>();
            Assert.IsNotNull(settingsViewModel, "settingsViewModel");
        }
    }
}
