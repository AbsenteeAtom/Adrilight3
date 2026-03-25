using System.ComponentModel;

namespace adrilight.Util
{
    /// <summary>
    /// Exposes the runtime BPM detection state computed by <see cref="AudioCaptureReader"/>.
    /// Consumed by <see cref="adrilight.ViewModel.SettingsViewModel"/> for UI binding.
    /// </summary>
    public interface IBpmDetector : INotifyPropertyChanged
    {
        /// <summary>Most recently detected tempo in BPM, or 0 when no lock has been achieved.</summary>
        int DetectedBpm { get; }

        /// <summary>Detection confidence, 0..1.  Values ≥ 0.5 indicate a usable lock.</summary>
        float BpmConfidence { get; }

        /// <summary>Human-readable status string suitable for display in the UI.</summary>
        string BpmStatusText { get; }
    }
}
