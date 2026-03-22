using adrilight.ViewModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NLog;

namespace adrilight.Tests
{
    [TestClass]
    public class DiagnosticsViewModelTests
    {
        private static LogEntry MakeEntry(LogLevel level, string message = "test")
            => new LogEntry(System.DateTime.Now, level, "adrilight.Test", message);

        [TestMethod]
        public void InfoEntry_DoesNotSetWarningStatus()
        {
            var vm = new DiagnosticsViewModel();
            vm.AddEntryCore(MakeEntry(LogLevel.Info));
            Assert.AreEqual(DiagnosticStatus.Ok, vm.Status);
        }

        [TestMethod]
        public void WarnEntry_SetsWarningStatus()
        {
            var vm = new DiagnosticsViewModel();
            vm.AddEntryCore(MakeEntry(LogLevel.Warn));
            Assert.AreEqual(DiagnosticStatus.Warning, vm.Status);
        }

        [TestMethod]
        public void ErrorEntry_SetsErrorStatus()
        {
            var vm = new DiagnosticsViewModel();
            vm.AddEntryCore(MakeEntry(LogLevel.Error));
            Assert.AreEqual(DiagnosticStatus.Error, vm.Status);
        }

        [TestMethod]
        public void WarnAfterError_StatusRemainsError()
        {
            var vm = new DiagnosticsViewModel();
            vm.AddEntryCore(MakeEntry(LogLevel.Error));
            vm.AddEntryCore(MakeEntry(LogLevel.Warn));
            Assert.AreEqual(DiagnosticStatus.Error, vm.Status);
        }

        [TestMethod]
        public void ErrorAfterWarn_StatusBecomesError()
        {
            var vm = new DiagnosticsViewModel();
            vm.AddEntryCore(MakeEntry(LogLevel.Warn));
            vm.AddEntryCore(MakeEntry(LogLevel.Error));
            Assert.AreEqual(DiagnosticStatus.Error, vm.Status);
        }

        [TestMethod]
        public void Acknowledge_ResetsStatusToOk()
        {
            var vm = new DiagnosticsViewModel();
            vm.AddEntryCore(MakeEntry(LogLevel.Error));
            vm.Acknowledge();
            Assert.AreEqual(DiagnosticStatus.Ok, vm.Status);
        }

        [TestMethod]
        public void RingBuffer_CapsAt200Entries()
        {
            var vm = new DiagnosticsViewModel();
            for (int i = 0; i < 210; i++)
                vm.AddEntryCore(MakeEntry(LogLevel.Info, $"msg {i}"));
            Assert.AreEqual(200, vm.Entries.Count);
        }

        [TestMethod]
        public void FilterWarnPlus_ExcludesInfoEntries()
        {
            var vm = new DiagnosticsViewModel();
            vm.AddEntryCore(MakeEntry(LogLevel.Info, "info msg"));
            vm.AddEntryCore(MakeEntry(LogLevel.Warn, "warn msg"));
            vm.FilterLevel = 1; // Warn+
            Assert.AreEqual(1, vm.FilteredEntries.Count);
            Assert.AreEqual(LogLevel.Warn, vm.FilteredEntries[0].Level);
        }

        [TestMethod]
        public void FilterErrorPlus_ExcludesWarnEntries()
        {
            var vm = new DiagnosticsViewModel();
            vm.AddEntryCore(MakeEntry(LogLevel.Warn, "warn msg"));
            vm.AddEntryCore(MakeEntry(LogLevel.Error, "error msg"));
            vm.FilterLevel = 2; // Error+
            Assert.AreEqual(1, vm.FilteredEntries.Count);
            Assert.AreEqual(LogLevel.Error, vm.FilteredEntries[0].Level);
        }

        [TestMethod]
        public void FilterAll_ShowsAllEntries()
        {
            var vm = new DiagnosticsViewModel();
            vm.AddEntryCore(MakeEntry(LogLevel.Info));
            vm.AddEntryCore(MakeEntry(LogLevel.Warn));
            vm.AddEntryCore(MakeEntry(LogLevel.Error));
            vm.FilterLevel = 0; // All
            Assert.AreEqual(3, vm.FilteredEntries.Count);
        }
    }
}
