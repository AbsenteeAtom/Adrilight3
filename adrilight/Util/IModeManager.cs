namespace adrilight.Util
{
    /// <summary>Identifies the active lighting pipeline.</summary>
    public enum LightingMode
    {
        ScreenCapture,
        SoundToLight,
        GamerMode
    }

    /// <summary>
    /// Controls the active lighting mode and manages system-level inhibitors that
    /// temporarily suppress LED output (sleep, session lock, screen saver).
    ///
    /// ModeManager is the sole writer of <see cref="IUserSettings.TransferActive"/>.
    /// All code that needs to pause or resume LEDs must go through this interface
    /// rather than writing TransferActive directly. This ensures inhibitor sources
    /// (sleep, lock, screen saver) are tracked independently and do not overwrite
    /// each other's saved state.
    /// </summary>
    public interface IModeManager
    {
        /// <summary>The currently selected lighting mode. Always defaults to ScreenCapture on launch.</summary>
        LightingMode ActiveMode { get; }

        /// <summary>True if one or more system inhibitors are active.</summary>
        bool IsInhibited { get; }

        /// <summary>True if the user wants LEDs on and no inhibitor is active.</summary>
        bool IsOutputActive { get; }

        /// <summary>Switch to a different lighting mode. No-op if already on that mode.</summary>
        void SetMode(LightingMode mode);

        /// <summary>
        /// Add a named inhibitor. The first inhibitor saves user intent and forces
        /// TransferActive to false. Subsequent inhibitors are tracked but do not
        /// overwrite the saved intent. Conventional source names: "sleep", "lock", "screensaver".
        /// </summary>
        void AddInhibitor(string source);

        /// <summary>
        /// Remove a named inhibitor. When the last inhibitor is removed, TransferActive
        /// is restored to the user's saved intent.
        /// </summary>
        void RemoveInhibitor(string source);
    }
}
