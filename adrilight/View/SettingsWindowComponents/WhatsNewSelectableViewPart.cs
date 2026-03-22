using adrilight.ViewModel;
using System;

namespace adrilight.View.SettingsWindowComponents
{
    public class WhatsNewSetupSelectableViewPart : ISelectableViewPart
    {
        private readonly Lazy<WhatsNew> lazyContent;

        public WhatsNewSetupSelectableViewPart(Lazy<WhatsNew> lazyContent)
        {
            this.lazyContent = lazyContent ?? throw new ArgumentNullException(nameof(lazyContent));
        }

        public int Order => -50;
        public string ViewPartName => "What's New";
        public object Content => lazyContent.Value;
    }
}
