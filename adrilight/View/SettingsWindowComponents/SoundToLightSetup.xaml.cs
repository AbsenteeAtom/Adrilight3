using adrilight.ViewModel;
using System;
using System.Windows.Controls;

namespace adrilight.View.SettingsWindowComponents
{
    public partial class SoundToLightSetup : UserControl
    {
        public SoundToLightSetup()
        {
            InitializeComponent();
        }

        public class SoundToLightSelectableViewPart : ISelectableViewPart
        {
            private readonly Lazy<SoundToLightSetup> _lazyContent;

            public SoundToLightSelectableViewPart(Lazy<SoundToLightSetup> lazyContent)
            {
                _lazyContent = lazyContent ?? throw new ArgumentNullException(nameof(lazyContent));
            }

            public int Order => 75;  // after LightingModeSetup (60), before ComPortSetup (100)
            public string ViewPartName => "Sound to Light";
            public object Content => _lazyContent.Value;
        }
    }
}
