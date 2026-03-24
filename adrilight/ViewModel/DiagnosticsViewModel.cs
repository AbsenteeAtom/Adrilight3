using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace adrilight.ViewModel
{
    public enum DiagnosticStatus { Ok = 0, Warning = 1, Error = 2 }

    public class DiagnosticsViewModel : ObservableObject
    {
        private const int MaxEntries = 200;

        public DiagnosticsViewModel()
        {
            AcknowledgeCommand      = new RelayCommand(Acknowledge);
            CopyToClipboardCommand  = new RelayCommand(CopyToClipboard);
        }

        // ── Entries ──────────────────────────────────────────────────────────

        public ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();
        public ObservableCollection<LogEntry> FilteredEntries { get; } = new ObservableCollection<LogEntry>();

        // ── Filter ────────────────────────────────────────────────────────────

        private int _filterLevel = 0;  // 0=All  1=Warn+  2=Error+
        public int FilterLevel
        {
            get => _filterLevel;
            set
            {
                if (SetProperty(ref _filterLevel, value))
                {
                    OnPropertyChanged(nameof(FilterAll));
                    OnPropertyChanged(nameof(FilterWarnPlus));
                    OnPropertyChanged(nameof(FilterErrorPlus));
                    RebuildFilter();
                }
            }
        }

        public bool FilterAll      { get => _filterLevel == 0; set { if (value) FilterLevel = 0; } }
        public bool FilterWarnPlus { get => _filterLevel == 1; set { if (value) FilterLevel = 1; } }
        public bool FilterErrorPlus{ get => _filterLevel == 2; set { if (value) FilterLevel = 2; } }

        // ── Status indicator ─────────────────────────────────────────────────

        private DiagnosticStatus _status = DiagnosticStatus.Ok;
        public DiagnosticStatus Status
        {
            get => _status;
            private set
            {
                if (SetProperty(ref _status, value))
                {
                    OnPropertyChanged(nameof(StatusTooltip));
                }
            }
        }

        public string StatusTooltip => Status switch
        {
            DiagnosticStatus.Warning => "Warnings recorded — click to view diagnostics",
            DiagnosticStatus.Error   => "Errors recorded — click to view diagnostics",
            _                        => "System status: OK"
        };

        // ── Night Light status ────────────────────────────────────────────────

        private string _nightLightConfidenceDisplay = "Night Light: —";
        public string NightLightConfidenceDisplay
        {
            get => _nightLightConfidenceDisplay;
            set => SetProperty(ref _nightLightConfidenceDisplay, value);
        }

        /// <summary>Called by SettingsViewModel when NightLightDetection produces a new prediction.</summary>
        public void UpdateNightLightDisplay(string display)
        {
            NightLightConfidenceDisplay = display;
        }

        // ── Commands ──────────────────────────────────────────────────────────

        public ICommand AcknowledgeCommand     { get; }
        public ICommand CopyToClipboardCommand { get; }

        public void Acknowledge()
        {
            Status = DiagnosticStatus.Ok;
        }

        private void CopyToClipboard()
        {
            var sb = new StringBuilder();
            foreach (var e in FilteredEntries.Reverse())
                sb.AppendLine($"{e.Timestamp:yyyy-MM-dd HH:mm:ss}  {e.LevelDisplay,-5}  {e.ShortLogger,-30}  {e.Message}");
            Clipboard.SetText(sb.ToString());
        }

        // ── Entry ingestion ───────────────────────────────────────────────────

        /// <summary>
        /// Thread-safe entry point called by the NLog target from any thread.
        /// Marshals to the UI thread if needed.
        /// </summary>
        public void AddEntry(LogEntry entry)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.BeginInvoke(new System.Action(() => AddEntryCore(entry)));
            else
                AddEntryCore(entry);
        }

        /// <summary>
        /// Core logic — must be called on the UI thread. Internal for unit testing.
        /// </summary>
        internal void AddEntryCore(LogEntry entry)
        {
            Entries.Insert(0, entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(Entries.Count - 1);

            // Ratchet status up — never downgrade within a session
            if (entry.Level >= LogLevel.Error)
                Status = DiagnosticStatus.Error;
            else if (entry.Level >= LogLevel.Warn && Status != DiagnosticStatus.Error)
                Status = DiagnosticStatus.Warning;

            // Add to filtered view if it passes current filter
            var threshold = FilterThreshold;
            if (entry.Level >= threshold)
                FilteredEntries.Insert(0, entry);

            // Trim filtered view to match MaxEntries
            while (FilteredEntries.Count > MaxEntries)
                FilteredEntries.RemoveAt(FilteredEntries.Count - 1);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private LogLevel FilterThreshold => _filterLevel switch
        {
            1 => LogLevel.Warn,
            2 => LogLevel.Error,
            _ => LogLevel.Trace
        };

        private void RebuildFilter()
        {
            var threshold = FilterThreshold;
            FilteredEntries.Clear();
            foreach (var e in Entries)
            {
                if (e.Level >= threshold)
                    FilteredEntries.Add(e);
            }
        }
    }
}
