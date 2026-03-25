using adrilight.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using System;

namespace adrilight
{
    internal class UserSettings : ObservableObject, IUserSettings
    {
        private bool _autostart = false;
        private int _borderDistanceX = 0;
        private int _borderDistanceY = 100;
        private string _comPort = null;
        private string _adrilightVersion = "2.0.7";
        private bool _mirrorX = false;
        private bool _mirrorY = false;
        private int _offsetLed = 0;
        private bool _isPreviewEnabled = false;
        private byte _saturationTreshold = 10;
        private int _spotHeight = 50;
        private int _spotsX = 5;
        private int _spotsY = 7;
        private int _spotWidth = 50;
        private bool _startMinimized = false;
        private bool _transferActive = false;
        private bool _useLinearLighting = false;

        private byte _whitebalanceRed = 100;
        private byte _whitebalanceGreen = 100;
        private byte _whitebalanceBlue = 100;

        private byte _altWhitebalanceRed = 100;
        private byte _altWhitebalanceGreen = 100;
        private byte _altWhitebalanceBlue = 80;

        private bool _sendRandomColors = false;
        private int _limitFps = 60;
        private int _baudRate = 1000000;
        private bool _blackBarDetectionEnabled = true;
        private byte _blackBarLuminanceThreshold = 20;
        private bool _sleepWakeAwarenessEnabled = true;
        private int _configFileVersion = 3;
        private AlternateWhiteBalanceModeEnum _alternateWhiteBalanceMode = AlternateWhiteBalanceModeEnum.Off;
        private byte _soundToLightSensitivity = 50;
        private byte _soundToLightSmoothing = 50;
        private int  _soundToLightMaxBpm = 120;
        private float _soundToLightRedGain   = 0.6f;
        private float _soundToLightGreenGain = 1.05f;
        private float _soundToLightBlueGain  = 1.5f;

        public int ConfigFileVersion { get => _configFileVersion; set => SetProperty(ref _configFileVersion, value); }
        public bool Autostart { get => _autostart; set => SetProperty(ref _autostart, value); }
        public int BorderDistanceX { get => _borderDistanceX; set => SetProperty(ref _borderDistanceX, value); }
        public int BorderDistanceY { get => _borderDistanceY; set => SetProperty(ref _borderDistanceY, value); }
        public string ComPort { get => _comPort; set => SetProperty(ref _comPort, value); }
        public string AdrilightVersion { get => _adrilightVersion; set => SetProperty(ref _adrilightVersion, value); }
        public bool MirrorX { get => _mirrorX; set => SetProperty(ref _mirrorX, value); }
        public bool MirrorY { get => _mirrorY; set => SetProperty(ref _mirrorY, value); }
        public int OffsetLed { get => _offsetLed; set => SetProperty(ref _offsetLed, value); }
        public int LimitFps { get => _limitFps; set => SetProperty(ref _limitFps, value); }
        public bool IsPreviewEnabled { get => _isPreviewEnabled; set => SetProperty(ref _isPreviewEnabled, value); }
        public byte SaturationTreshold { get => _saturationTreshold; set => SetProperty(ref _saturationTreshold, value); }
        public int SpotHeight { get => _spotHeight; set => SetProperty(ref _spotHeight, value); }
        public int SpotsX { get => _spotsX; set => SetProperty(ref _spotsX, value); }
        public int SpotsY { get => _spotsY; set => SetProperty(ref _spotsY, value); }
        public int SpotWidth { get => _spotWidth; set => SetProperty(ref _spotWidth, value); }
        public bool StartMinimized { get => _startMinimized; set => SetProperty(ref _startMinimized, value); }
        public bool TransferActive { get => _transferActive; set => SetProperty(ref _transferActive, value); }
        public bool UseLinearLighting { get => _useLinearLighting; set => SetProperty(ref _useLinearLighting, value); }
        public byte WhitebalanceRed { get => _whitebalanceRed; set => SetProperty(ref _whitebalanceRed, Clamp(value)); }
        public byte WhitebalanceGreen { get => _whitebalanceGreen; set => SetProperty(ref _whitebalanceGreen, Clamp(value)); }
        public byte WhitebalanceBlue { get => _whitebalanceBlue; set => SetProperty(ref _whitebalanceBlue, Clamp(value)); }
        public byte AltWhitebalanceRed { get => _altWhitebalanceRed; set => SetProperty(ref _altWhitebalanceRed, Clamp(value)); }
        public byte AltWhitebalanceGreen { get => _altWhitebalanceGreen; set => SetProperty(ref _altWhitebalanceGreen, Clamp(value)); }
        public byte AltWhitebalanceBlue { get => _altWhitebalanceBlue; set => SetProperty(ref _altWhitebalanceBlue, Clamp(value)); }

        private static byte Clamp(byte value) => Math.Clamp(value, (byte)1, (byte)100);
        public bool SendRandomColors { get => _sendRandomColors; set => SetProperty(ref _sendRandomColors, value); }
        public int BaudRate { get => _baudRate; set => SetProperty(ref _baudRate, value); }
        public bool BlackBarDetectionEnabled { get => _blackBarDetectionEnabled; set => SetProperty(ref _blackBarDetectionEnabled, value); }
        public byte BlackBarLuminanceThreshold { get => _blackBarLuminanceThreshold; set => SetProperty(ref _blackBarLuminanceThreshold, value); }
        public bool SleepWakeAwarenessEnabled { get => _sleepWakeAwarenessEnabled; set => SetProperty(ref _sleepWakeAwarenessEnabled, value); }
        public Guid InstallationId { get; set; } = Guid.NewGuid();
        public AlternateWhiteBalanceModeEnum AlternateWhiteBalanceMode { get => _alternateWhiteBalanceMode; set => SetProperty(ref _alternateWhiteBalanceMode, value); }
        public byte SoundToLightSensitivity { get => _soundToLightSensitivity; set => SetProperty(ref _soundToLightSensitivity, value); }
        public byte SoundToLightSmoothing { get => _soundToLightSmoothing; set => SetProperty(ref _soundToLightSmoothing, value); }
        public int  SoundToLightMaxBpm { get => _soundToLightMaxBpm; set => SetProperty(ref _soundToLightMaxBpm, value); }
        public float SoundToLightRedGain   { get => _soundToLightRedGain;   set => SetProperty(ref _soundToLightRedGain,   value); }
        public float SoundToLightGreenGain { get => _soundToLightGreenGain; set => SetProperty(ref _soundToLightGreenGain, value); }
        public float SoundToLightBlueGain  { get => _soundToLightBlueGain;  set => SetProperty(ref _soundToLightBlueGain,  value); }
    }
}