using System;

namespace adrilight.Util
{
    /// <summary>
    /// Abstraction over the system audio capture device.
    /// Provides a testable seam between AudioCaptureReader and WasapiLoopbackCapture.
    /// SampleRate and Channels are valid after Start() is called.
    /// </summary>
    public interface IAudioCaptureProvider
    {
        /// <summary>Sample rate of the capture device in Hz. Valid after Start().</summary>
        int SampleRate { get; }

        /// <summary>Number of interleaved channels in the sample stream. Valid after Start().</summary>
        int Channels { get; }

        /// <summary>
        /// Begin capturing audio. Calls <paramref name="onDataAvailable"/> on the capture thread
        /// with a float array of interleaved channel samples and the sample count.
        /// </summary>
        void Start(Action<float[], int> onDataAvailable);

        /// <summary>Stop capturing audio and release the device.</summary>
        void Stop();
    }
}
