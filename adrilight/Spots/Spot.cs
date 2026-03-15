using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace adrilight
{
    [DebuggerDisplay("Spot: Rectangle={Rectangle}, Color={Red},{Green},{Blue}")]
    sealed class Spot : ObservableObject, IDisposable, ISpot
    {
        public Spot(int top, int left, int width, int height)
        {
            Rectangle = new Rectangle(top, left, width, height);
        }

        public Rectangle Rectangle { get; private set; }

        private bool _isFirst;
        public bool IsFirst
        {
            get => _isFirst;
            set => SetProperty(ref _isFirst, value);
        }

        public Color OnDemandColor => Color.FromRgb(Red, Green, Blue);
        public Color OnDemandColorTransparent => Color.FromArgb(0, Red, Green, Blue);

        public byte Red { get; private set; }
        public byte Green { get; private set; }
        public byte Blue { get; private set; }

        public void SetColor(byte red, byte green, byte blue, bool raiseEvents)
        {
            Red = red;
            Green = green;
            Blue = blue;
            _lastMissingValueIndication = null;

            if (raiseEvents)
            {
                OnPropertyChanged(nameof(OnDemandColor));
                OnPropertyChanged(nameof(OnDemandColorTransparent));
            }
        }

        public void Dispose() { }

        private DateTime? _lastMissingValueIndication;
        private readonly double _dimToBlackIntervalInMs = TimeSpan.FromMilliseconds(10000).TotalMilliseconds;
        private float _dimR, _dimG, _dimB;

        public void IndicateMissingValue()
        {
            var localCopyLastMissingValueIndication = _lastMissingValueIndication;

            if (!localCopyLastMissingValueIndication.HasValue)
            {
                _dimR = Red;
                _dimG = Green;
                _dimB = Blue;
                localCopyLastMissingValueIndication = _lastMissingValueIndication = DateTime.UtcNow;
            }

            var dimFactor = (float)(1 - (DateTime.UtcNow - localCopyLastMissingValueIndication.Value).TotalMilliseconds / _dimToBlackIntervalInMs);
            dimFactor = Math.Max(0, Math.Min(1, dimFactor));

            SetColor((byte)(dimFactor * _dimR), (byte)(dimFactor * _dimG), (byte)(dimFactor * _dimB), true);
        }
    }
}