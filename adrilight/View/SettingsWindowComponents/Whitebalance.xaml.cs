using adrilight.ViewModel;
using System;
using System.Windows.Controls;

namespace adrilight.View.SettingsWindowComponents
{
    /// <summary>
    /// Interaction logic for LightingMode.xaml
    /// </summary>
    public partial class Whitebalance : UserControl
    {
        public Whitebalance()
        {
            InitializeComponent();
        }



        public class WhitebalanceSelectableViewPart : ISelectableViewPart
        {
            private readonly Lazy<Whitebalance> lazyContent;

            public WhitebalanceSelectableViewPart(Lazy<Whitebalance> lazyContent)
            {
                this.lazyContent = lazyContent ?? throw new ArgumentNullException(nameof(lazyContent));
            }

            public int Order => 800;

            public string ViewPartName => "White Balance";

            public object Content { get => lazyContent.Value; }
        }
    }
}
