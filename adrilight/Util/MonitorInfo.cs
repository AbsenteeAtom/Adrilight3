namespace adrilight.Util
{
    /// <summary>
    /// Represents a single enumerated display output available for screen capture.
    /// </summary>
    public sealed class MonitorInfo
    {
        public int AdapterIndex { get; }
        public int OutputIndex { get; }
        public string DisplayLabel { get; }

        public MonitorInfo(int adapterIndex, int outputIndex, string displayLabel)
        {
            AdapterIndex = adapterIndex;
            OutputIndex = outputIndex;
            DisplayLabel = displayLabel;
        }

        public override string ToString() => DisplayLabel;
    }
}
