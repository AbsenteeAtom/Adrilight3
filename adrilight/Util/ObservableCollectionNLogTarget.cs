using adrilight.ViewModel;
using NLog;
using NLog.Targets;

namespace adrilight.Util
{
    /// <summary>
    /// NLog target that pushes log entries into DiagnosticsViewModel's in-memory ring buffer.
    /// Registered programmatically in App.SetupLogging().
    /// </summary>
    [Target("ObservableCollection")]
    public sealed class ObservableCollectionNLogTarget : Target
    {
        private readonly DiagnosticsViewModel _vm;

        public ObservableCollectionNLogTarget(DiagnosticsViewModel vm)
        {
            Name = "ObservableCollection";
            _vm = vm;
        }

        protected override void Write(LogEventInfo logEvent)
        {
            var entry = new LogEntry(
                logEvent.TimeStamp,
                logEvent.Level,
                logEvent.LoggerName,
                logEvent.FormattedMessage);

            _vm.AddEntry(entry);
        }
    }
}
