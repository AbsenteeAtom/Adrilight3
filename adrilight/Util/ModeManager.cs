using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using NLog;

namespace adrilight.Util
{
    /// <summary>
    /// Manages the active lighting mode and the set of system inhibitors that suppress LED output.
    ///
    /// Inhibitor model:
    ///   When the first inhibitor is added, the user's current TransferActive state is recorded
    ///   and TransferActive is forced to false. When the last inhibitor is removed, TransferActive
    ///   is restored to the recorded state. Multiple inhibitors can be active simultaneously; only
    ///   the outermost add and remove change TransferActive, so sources never overwrite each other.
    ///
    /// Mode model (MVP — Screen Capture only):
    ///   ActiveMode defaults to ScreenCapture on every launch and is never persisted.
    ///   DesktopDuplicatorReader reacts to TransferActive via PropertyChanged and needs no explicit
    ///   Start/Stop here. When Sound to Light or Gamer Mode are implemented, SetMode() will stop
    ///   the current pipeline and start the new one.
    /// </summary>
    internal sealed class ModeManager : IModeManager
    {
        private static readonly ILogger _log = LogManager.GetCurrentClassLogger();

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly IUserSettings _settings;
        private readonly Dictionary<LightingMode, ILightingMode> _lightingModes;
        private readonly HashSet<string> _inhibitors = new HashSet<string>();
        private LightingMode _activeMode = LightingMode.ScreenCapture;

        // The user's desired LED state. Captured when the first inhibitor is added and
        // restored when the last inhibitor is removed. Updated whenever external code
        // writes TransferActive directly (tray toggle, UI toggle) so user intent is
        // always current at the time inhibition is lifted.
        private bool _userTransferActive;

        // Guard flag: prevents our own PropertyChanged handler from treating a
        // ModeManager-initiated write to TransferActive as external user input.
        private bool _writingTransferActive;

        public ModeManager(IUserSettings settings, IEnumerable<ILightingMode> lightingModes)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _lightingModes = (lightingModes ?? Enumerable.Empty<ILightingMode>())
                .ToDictionary(m => m.ModeId);
            _userTransferActive = settings.TransferActive;
            settings.PropertyChanged += OnSettingsPropertyChanged;
        }

        private void OnSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IUserSettings.TransferActive) || _writingTransferActive)
                return;

            // An external caller (tray icon, UI toggle, TCP ON/OFF) wrote TransferActive directly.
            // Record it as the new user intent. If currently inhibited, snap it back to false
            // immediately so inhibition always takes priority over external writes.
            _userTransferActive = _settings.TransferActive;
            if (IsInhibited)
                SetTransferActive(false);
        }

        public LightingMode ActiveMode => _activeMode;
        public bool IsInhibited => _inhibitors.Count > 0;
        public bool IsOutputActive => !IsInhibited && _userTransferActive;

        public void SetMode(LightingMode mode)
        {
            if (_activeMode == mode) return;
            _log.Info($"Mode switching from {_activeMode} to {mode}");

            if (_lightingModes.TryGetValue(_activeMode, out var outgoing) && outgoing.IsRunning)
                outgoing.Stop();

            _activeMode = mode;
            OnPropertyChanged(nameof(ActiveMode));

            if (_lightingModes.TryGetValue(_activeMode, out var incoming))
            {
                if (!incoming.IsRunning)
                    incoming.Start();
            }
            else
            {
                _log.Warn($"No ILightingMode pipeline registered for {_activeMode}.");
            }
        }

        public void AddInhibitor(string source)
        {
            bool wasEmpty = _inhibitors.Count == 0;
            _inhibitors.Add(source);
            if (wasEmpty)
            {
                _log.Info($"Inhibitor '{source}' added — pausing LEDs (user intent was {_userTransferActive}).");
                SetTransferActive(false);
            }
            else
            {
                _log.Debug($"Inhibitor '{source}' added ({_inhibitors.Count} active, already inhibited).");
            }
        }

        public void RemoveInhibitor(string source)
        {
            _inhibitors.Remove(source);
            if (_inhibitors.Count == 0)
            {
                _log.Info($"Inhibitor '{source}' removed — all clear, restoring LEDs to user intent ({_userTransferActive}).");
                SetTransferActive(_userTransferActive);
            }
            else
            {
                _log.Debug($"Inhibitor '{source}' removed ({_inhibitors.Count} remaining).");
            }
        }

        private void SetTransferActive(bool value)
        {
            _writingTransferActive = true;
            try { _settings.TransferActive = value; }
            finally { _writingTransferActive = false; }
        }
    }
}
