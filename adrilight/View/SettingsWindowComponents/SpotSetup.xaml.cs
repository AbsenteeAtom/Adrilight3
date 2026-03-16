using adrilight.ViewModel;
using System;
using System.Windows.Controls;

namespace adrilight.View.SettingsWindowComponents
{
    /// <summary>
    /// Interaction logic for LightingMode.xaml
    /// </summary>
    public partial class SpotSetup : UserControl
    {
        public SpotSetup()
        {
            InitializeComponent();
        }



        public class SpotSetupSelectableViewPart : ISelectableViewPart
        {
            private readonly Lazy<SpotSetup> lazyContent;

            public SpotSetupSelectableViewPart(Lazy<SpotSetup> lazyContent)
            {
                this.lazyContent = lazyContent ?? throw new ArgumentNullException(nameof(lazyContent));
            }

            public int Order => 50;

            public string ViewPartName => "Spot Detection Setup";

            public object Content { get => lazyContent.Value; }
        }
    }
}
