using adrilight.Settings;
using System;
using System.ComponentModel;

namespace adrilight
{
    public interface IUserSettings : INotifyPropertyChanged
    {
        int ConfigFileVersion { get; set; }
        bool Autostart { get; set; }
        int BorderDistanceX { get; set; }
        int BorderDistanceY { get; set; }
        string ComPort { get; set; }

        bool MirrorX { get; set; }
        bool MirrorY { get; set; }
        int OffsetLed { get; set; }

        bool IsPreviewEnabled { get; set; }
        byte SaturationTreshold { get; set; }
        int SpotHeight { get; set; }
        int SpotsX { get; set; }
        int SpotsY { get; set; }
        int SpotWidth { get; set; }
        bool StartMinimized { get; set; }
        bool TransferActive { get; set; }
        bool UseLinearLighting { get; set; }

        Guid InstallationId { get; set; }

        byte WhitebalanceRed { get; set; }
        byte WhitebalanceGreen { get; set; }
        byte WhitebalanceBlue { get; set; }

        byte AltWhitebalanceRed { get; set; }
        byte AltWhitebalanceGreen { get; set; }
        byte AltWhitebalanceBlue { get; set; }

        // DEBUG-ONLY: generates a per-frame rainbow in SerialStream, bypassing the spot pipeline entirely.
        // This is a hardware test feature and is not a LightingMode — do not promote it to one.
        bool SendRandomColors { get; set; }

        int LimitFps { get; set; }

        // Serial baud rate — must match the Arduino sketch (default 1000000).
        // Only change this if you recompile the Arduino sketch with a matching baud rate.
        int BaudRate { get; set; }

        // Black bar detection — remaps bar LEDs to nearest content edge instead of turning them off.
        bool BlackBarDetectionEnabled { get; set; }
        byte BlackBarLuminanceThreshold { get; set; }

        // Sleep/wake awareness — pauses LEDs on sleep, screensaver, or monitor-off; restores on wake.
        bool SleepWakeAwarenessEnabled { get; set; }

        string AdrilightVersion { get; set; }
        AlternateWhiteBalanceModeEnum AlternateWhiteBalanceMode { get; set; }

        // Sound to Light settings
        byte SoundToLightSensitivity { get; set; }
        byte SoundToLightSmoothing { get; set; }
        int  SoundToLightMaxBpm { get; set; }
        float SoundToLightRedGain { get; set; }
        float SoundToLightGreenGain { get; set; }
        float SoundToLightBlueGain { get; set; }
        bool  SoundToLightAutoBpm    { get; set; }
        bool  SoundToLightBandSpread { get; set; }
    }
}