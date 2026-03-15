using Ninject;
using System;

namespace adrilight.ViewModel
{
    internal class ViewModelLocator
    {
        private readonly IKernel kernel;

        public ViewModelLocator()
        {
            // Design-time constructor — uses fakes
            this.kernel = App.SetupDependencyInjection(true);
        }

        public ViewModelLocator(IKernel kernel)
        {
            this.kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        }

        public SettingsViewModel SettingsViewModel => kernel.Get<SettingsViewModel>();

        public static void Cleanup()
        {
            // TODO Clear the ViewModels
        }
    }
}