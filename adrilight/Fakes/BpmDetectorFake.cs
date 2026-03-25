using System.ComponentModel;
using adrilight.Util;

namespace adrilight.Fakes
{
    class BpmDetectorFake : IBpmDetector
    {
        public int    DetectedBpm   => 120;
        public float  BpmConfidence => 0.8f;
        public string BpmStatusText => "Detected: 120 BPM — Good lock";

#pragma warning disable CS0067
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067
    }
}
