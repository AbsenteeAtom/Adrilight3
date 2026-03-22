using adrilight.ViewModel;
using Microsoft.Win32;
using NLog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace adrilight.Util
{
    public enum NightLightState { Unknown, Off, On }

    /// <summary>Abstraction over the registry read, allowing unit tests to inject fake data.</summary>
    public interface INightLightRegistryReader
    {
        byte[] ReadData();
    }

    public class RegistryNightLightReader : INightLightRegistryReader
    {
        // Windows 10 1903+ and all Windows 11
        private const string KeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\" +
            @"default$windows.data.bluelightreduction.bluelightreductionstate\" +
            @"windows.data.bluelightreduction.bluelightreductionstate";

        public byte[] ReadData()
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            return key?.GetValue("Data") as byte[];
        }
    }

    class NightLightDetection
    {
        private readonly ILogger _log = LogManager.GetCurrentClassLogger();
        private readonly SettingsViewModel _settingsViewModel;
        private readonly INightLightRegistryReader _reader;

        public NightLightDetection(SettingsViewModel settingsViewModel,
            INightLightRegistryReader reader = null)
        {
            _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
            _reader = reader ?? new RegistryNightLightReader();

            _settingsViewModel.Settings.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(_settingsViewModel.Settings.AlternateWhiteBalanceMode))
                    ActOnce();
            };
            ActOnce();
        }

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private Task _actingTask;

        public void Start()
        {
            _actingTask = Task.Run(ActInLoop);
        }

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
            if (_actingTask == null || _actingTask.IsCompleted) return;
            _actingTask.Wait();
        }

        private async Task ActInLoop()
        {
            try
            {
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), _cancellationTokenSource.Token);
                    ActOnce();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        private readonly object _actOnceLock = new object();

        private void ActOnce()
        {
            lock (_actOnceLock)
            {
                NightLightState state;
                switch (_settingsViewModel.Settings.AlternateWhiteBalanceMode)
                {
                    case Settings.AlternateWhiteBalanceModeEnum.On:
                        state = NightLightState.On;
                        break;
                    case Settings.AlternateWhiteBalanceModeEnum.Off:
                        state = NightLightState.Off;
                        break;
                    default: // Auto
                        state = ReadNightLightState();
                        break;
                }
                _settingsViewModel.UpdateNightLightState(state);
            }
        }

        private byte[] _lastData;
        private NightLightState _lastState = NightLightState.Unknown;

        internal NightLightState ReadNightLightState()
        {
            var data = _reader.ReadData();

            if (data == null)
            {
                _log.Warn("Night Light registry path not found — detection unavailable. " +
                          "This may indicate a Windows update has changed the registry structure.");
                return NightLightState.Unknown;
            }

            // Skip re-parse if the registry data hasn't changed
            if (_lastData != null && DataEqual(data, _lastData))
                return _lastState;

            _lastData = data;
            _lastState = ParseRegistryData(data);

            var hex = BitConverter.ToString(data).Replace("-", " ");
            _log.Debug($"Night Light state: {_lastState} (byte[18]=0x{data[18]:X2}) blob={hex}");

            return _lastState;
        }

        /// <summary>
        /// Pure parsing function — testable without registry access or SettingsViewModel.
        /// Reads byte 18 of the CloudStore REG_BINARY blob.
        /// Known ON values: 0x15 (observed on some Windows builds), 0x12 (observed on others).
        /// Different Windows versions produce different base byte values at this position,
        /// but both indicate the Bond "enabled" field is present → Night Light ON.
        /// Any other value means the field is absent → Night Light OFF.
        /// Only null or too-short data returns Unknown.
        /// </summary>
        internal static NightLightState ParseRegistryData(byte[] data)
        {
            if (data == null || data.Length <= 18)
                return NightLightState.Unknown;

            return (data[18] == 0x15 || data[18] == 0x12) ? NightLightState.On : NightLightState.Off;
        }

        private static bool DataEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
