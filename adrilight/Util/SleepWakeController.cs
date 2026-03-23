namespace adrilight.Util
{
    /// <summary>
    /// Translates sleep/wake OS events into ModeManager inhibitor calls.
    /// Extracted from App.xaml.cs so the logic is unit-testable without a WPF host.
    ///
    /// On suspend: adds the "sleep" inhibitor to ModeManager, which forces TransferActive
    /// to false while preserving the user's intent for when the machine wakes.
    /// On resume: removes the "sleep" inhibitor; ModeManager restores TransferActive
    /// to the user's saved intent if no other inhibitors remain.
    /// </summary>
    internal sealed class SleepWakeController
    {
        private readonly IUserSettings _settings;
        private readonly IModeManager _modeManager;

        public SleepWakeController(IUserSettings settings, IModeManager modeManager)
        {
            _settings = settings;
            _modeManager = modeManager;
        }

        /// <summary>Called when the PC is about to sleep or the monitor is about to turn off.</summary>
        public void OnSuspend()
        {
            if (!_settings.SleepWakeAwarenessEnabled) return;
            _modeManager.AddInhibitor("sleep");
        }

        /// <summary>Called when the PC wakes or the monitor turns back on.</summary>
        public void OnResume()
        {
            if (!_settings.SleepWakeAwarenessEnabled) return;
            _modeManager.RemoveInhibitor("sleep");
        }
    }
}
