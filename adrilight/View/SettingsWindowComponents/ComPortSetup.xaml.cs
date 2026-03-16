using adrilight.ViewModel;
using System;
using System.Windows.Controls;

namespace adrilight.View.SettingsWindowComponents
{
    /// <summary>
    /// Interaction logic for ComPortSetup.xaml
    /// </summary>
    public partial class ComPortSetup : UserControl
    {
        public ComPortSetup()
        {
            InitializeComponent();
        }



        public class ComPortSetupSelectableViewPart : ISelectableViewPart
        {
            private readonly Lazy<ComPortSetup> lazyContent;

            public ComPortSetupSelectableViewPart(Lazy<ComPortSetup> lazyContent)
            {
                this.lazyContent = lazyContent ?? throw new ArgumentNullException(nameof(lazyContent));
            }

            public int Order => 100;

            public string ViewPartName => "Serial Communication Setup";

            public object Content { get => lazyContent.Value; }
        }
    }
}
