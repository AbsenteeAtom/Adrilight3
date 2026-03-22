using adrilight.ViewModel;
using System;

namespace adrilight.View.SettingsWindowComponents
{
    public class DiagnosticsSelectableViewPart : ISelectableViewPart
    {
        private readonly Lazy<Diagnostics> _lazyContent;

        public DiagnosticsSelectableViewPart(Lazy<Diagnostics> lazyContent)
        {
            _lazyContent = lazyContent ?? throw new ArgumentNullException(nameof(lazyContent));
        }

        public int Order => -45;  // after About (-50), before all other tabs
        public string ViewPartName => "Diagnostics";
        public object Content => _lazyContent.Value;
    }
}
