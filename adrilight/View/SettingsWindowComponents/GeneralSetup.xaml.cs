using adrilight.ViewModel;
using System;
using System.Windows.Controls;

namespace adrilight.View.SettingsWindowComponents
{
    /// <summary>
    /// Interaction logic for LightingMode.xaml
    /// </summary>
    public partial class GeneralSetup : UserControl
    {
        public GeneralSetup()
        {
            InitializeComponent();
        }



        public class GeneralSetupSelectableViewPart : ISelectableViewPart
        {
            private readonly Lazy<GeneralSetup> lazyContent;

            public GeneralSetupSelectableViewPart(Lazy<GeneralSetup> lazyContent)
            {
                this.lazyContent = lazyContent ?? throw new ArgumentNullException(nameof(lazyContent));
            }

            public int Order => 900;

            public string ViewPartName => "General Setup";

            public object Content { get => lazyContent.Value; }
        }
    }
}
