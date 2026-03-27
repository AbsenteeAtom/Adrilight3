using NLog;
using SharpDX.DXGI;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace adrilight.Util
{
    /// <summary>
    /// Enumerates available display outputs using DXGI, cross-referenced with
    /// System.Windows.Forms.Screen to obtain the primary flag and display index.
    ///
    /// Only outputs where IsAttachedToDesktop == true are returned.
    /// Falls back to a single default entry if DXGI enumeration fails (e.g. no GPU).
    ///
    /// Labels are of the form:
    ///   "Display 1 — 1920×1080 (Primary)"
    ///   "Display 2 — 2560×1440"
    /// </summary>
    internal static class MonitorEnumerator
    {
        private static readonly ILogger _log = LogManager.GetCurrentClassLogger();

        public static IReadOnlyList<MonitorInfo> Enumerate()
        {
            try
            {
                return EnumerateDxgi();
            }
            catch (Exception ex)
            {
                _log.Warn(ex, "DXGI monitor enumeration failed — falling back to single default entry.");
                return new[] { new MonitorInfo(0, 0, "Display 1 (default)") };
            }
        }

        private static IReadOnlyList<MonitorInfo> EnumerateDxgi()
        {
            var results = new List<MonitorInfo>();
            int displayNumber = 1;

            using var factory = new Factory1();
            int adapterCount = factory.GetAdapterCount1();

            for (int adapterIndex = 0; adapterIndex < adapterCount; adapterIndex++)
            {
                using var adapter = factory.GetAdapter1(adapterIndex);
                int outputCount = adapter.GetOutputCount();

                for (int outputIndex = 0; outputIndex < outputCount; outputIndex++)
                {
                    using var output = adapter.GetOutput(outputIndex);
                    var desc = output.Description;

                    if (!desc.IsAttachedToDesktop)
                        continue;

                    var screen = FindScreen(desc.DeviceName);
                    bool isPrimary = screen?.Primary ?? false;
                    var bounds = desc.DesktopBounds;
                    int width  = bounds.Right  - bounds.Left;
                    int height = bounds.Bottom - bounds.Top;

                    var label = isPrimary
                        ? $"Display {displayNumber} — {width}×{height} (Primary)"
                        : $"Display {displayNumber} — {width}×{height}";

                    results.Add(new MonitorInfo(adapterIndex, outputIndex, label));
                    displayNumber++;
                }
            }

            if (results.Count == 0)
            {
                _log.Warn("No attached desktop outputs found — falling back to single default entry.");
                return new[] { new MonitorInfo(0, 0, "Display 1 (default)") };
            }

            return results;
        }

        private static Screen FindScreen(string dxgiDeviceName)
        {
            // DXGI DeviceName is "\\.\DISPLAY1"; Screen.DeviceName is "\\.\DISPLAY1" — match directly.
            foreach (var screen in Screen.AllScreens)
            {
                if (string.Equals(screen.DeviceName, dxgiDeviceName, StringComparison.OrdinalIgnoreCase))
                    return screen;
            }
            return null;
        }
    }
}
