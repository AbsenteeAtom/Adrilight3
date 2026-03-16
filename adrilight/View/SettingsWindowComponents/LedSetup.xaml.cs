using adrilight.ViewModel;
using System;
using System.Windows.Controls;

namespace adrilight.View.SettingsWindowComponents
{
    /// <summary>
    /// Interaction logic for LedSetup.xaml
    /// </summary>
    public partial class LedSetup : UserControl
    {
        public LedSetup()
        {
            InitializeComponent();
        }

        public class LedSetupSelectableViewPart : ISelectableViewPart
        {
            private readonly Lazy<LedSetup> lazyContent;

            public LedSetupSelectableViewPart(Lazy<LedSetup> lazyContent)
            {
                this.lazyContent = lazyContent ?? throw new ArgumentNullException(nameof(lazyContent));
            }
            public int Order => 10;

            public string ViewPartName => "Physical LED Setup";

            public object Content { get => lazyContent.Value; }
        }
    }
}
