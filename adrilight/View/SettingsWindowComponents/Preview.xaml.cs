using adrilight.ViewModel;
using System;
using System.Windows.Controls;

namespace adrilight.View.SettingsWindowComponents
{
    /// <summary>
    /// Interaction logic for LightingMode.xaml
    /// </summary>
    public partial class Preview : UserControl
    {
        public Preview()
        {
            InitializeComponent();
        }



        public class PreviewSelectableViewPart : ISelectableViewPart
        {
            private readonly Lazy<Preview> lazyContent;

            public PreviewSelectableViewPart(Lazy<Preview> lazyContent)
            {
                this.lazyContent = lazyContent ?? throw new ArgumentNullException(nameof(lazyContent));
            }

            public int Order => 700;

            public string ViewPartName => "Preview Results";

            public object Content { get => lazyContent.Value; }
        }
    }
}
