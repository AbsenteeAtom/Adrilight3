namespace adrilight.Util
{
    /// <summary>
    /// Represents a self-contained lighting pipeline (screen capture, audio reactive, etc.).
    /// Implementations are discovered by Ninject convention binding and injected into ModeManager.
    /// </summary>
    public interface ILightingMode
    {
        /// <summary>The mode this pipeline serves.</summary>
        LightingMode ModeId { get; }

        /// <summary>Start the pipeline. Called by ModeManager when switching to this mode.</summary>
        void Start();

        /// <summary>Stop the pipeline. Called by ModeManager when switching away from this mode.</summary>
        void Stop();

        /// <summary>True if the pipeline is currently running.</summary>
        bool IsRunning { get; }
    }
}
