using System;
using NAudio.Wave;
using NLog;

namespace adrilight.Util
{
    /// <summary>
    /// Captures system audio output using WASAPI loopback.
    /// The audio device is not accessed until Start() is called, making the
    /// constructor safe for dependency injection in all environments.
    /// </summary>
    public sealed class WasapiAudioCaptureProvider : IAudioCaptureProvider, IDisposable
    {
        private static readonly ILogger _log = LogManager.GetCurrentClassLogger();

        private WasapiLoopbackCapture _capture;
        private Action<float[], int> _callback;

        public int SampleRate { get; private set; }
        public int Channels { get; private set; }

        public void Start(Action<float[], int> onDataAvailable)
        {
            _callback = onDataAvailable ?? throw new ArgumentNullException(nameof(onDataAvailable));
            _capture = new WasapiLoopbackCapture();
            SampleRate = _capture.WaveFormat.SampleRate;
            Channels = _capture.WaveFormat.Channels;
            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();
            _log.Info($"WASAPI loopback capture started: {SampleRate} Hz, {Channels} ch.");
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0 || _callback == null) return;
            int floatCount = e.BytesRecorded / sizeof(float);
            float[] samples = new float[floatCount];
            Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
            _callback(samples, floatCount);
        }

        public void Stop()
        {
            if (_capture == null) return;
            _capture.StopRecording();
            _capture.Dispose();
            _capture = null;
            _log.Info("WASAPI loopback capture stopped.");
        }

        public void Dispose() => Stop();
    }
}
