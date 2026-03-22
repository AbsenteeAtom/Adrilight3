using NLog;
using System;

namespace adrilight.ViewModel
{
    /// <summary>
    /// Represents a single in-memory log entry captured by ObservableCollectionNLogTarget.
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; }
        public LogLevel Level { get; }
        public string ShortLogger { get; }
        public string Message { get; }

        public LogEntry(DateTime timestamp, LogLevel level, string logger, string message)
        {
            Timestamp = timestamp;
            Level = level;
            var parts = logger?.Split('.');
            ShortLogger = (parts != null && parts.Length > 0) ? parts[parts.Length - 1] : logger ?? "";
            Message = message ?? "";
        }

        public string TimestampDisplay => Timestamp.ToString("HH:mm:ss");
        public string LevelDisplay => Level.Name.ToUpperInvariant();
    }
}
