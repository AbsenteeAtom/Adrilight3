namespace adrilight.Util
{
    /// <summary>
    /// Manages automatic LED pause/resume across sleep, wake, screensaver, and monitor-off events.
    /// Extracted from App.xaml.cs so the state machine can be unit-tested without a WPF host.
    /// </summary>
    internal sealed class SleepWakeController
    {
        private readonly IUserSettings _settings;
        private bool _wasActive;

        public SleepWakeController(IUserSettings settings) => _settings = settings;

        /// <summary>Called when the PC is about to sleep or the monitor is about to turn off.</summary>
        public void OnSuspend()
        {
            if (!_settings.SleepWakeAwarenessEnabled) return;
            _wasActive = _settings.TransferActive;
            _settings.TransferActive = false;
        }

        /// <summary>Called when the PC wakes or the monitor turns back on.</summary>
        public void OnResume()
        {
            if (!_settings.SleepWakeAwarenessEnabled) return;
            if (_wasActive)
                _settings.TransferActive = true;
        }
    }
}
