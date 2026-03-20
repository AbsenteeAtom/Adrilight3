using adrilight.ViewModel;
using System;
using System.Windows.Controls;

namespace adrilight.View.SettingsWindowComponents
{
    /// <summary>
    /// Interaction logic for LightingMode.xaml
    /// </summary>
    public partial class LightingModeSetup : UserControl
    {
        public LightingModeSetup()
        {
            InitializeComponent();
        }



        public class LightingModeSetupSelectableViewPart : ISelectableViewPart
        {
            private readonly Lazy<LightingModeSetup> lazyContent;

            public LightingModeSetupSelectableViewPart(Lazy<LightingModeSetup> lazyContent)
            {
                this.lazyContent = lazyContent ?? throw new ArgumentNullException(nameof(lazyContent));
            }

            public int Order => 60;

            public string ViewPartName => "Lighting Mode Selection";

            public object Content { get => lazyContent.Value; }
        }
    }
}
