using System;
using System.Threading;
using Microsoft.Windows.AppLifecycle;
using Microsoft.UI.Dispatching;

namespace Notifier
{
    public static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

            // Find or register the key instance
            var keyInstance = AppInstance.FindOrRegisterForKey("SiteUpdateNotifierSingleInstanceKey");

            if (!keyInstance.IsCurrent)
            {
                // Redirect to the existing running instance
                var activatedEventArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                keyInstance.RedirectActivationToAsync(activatedEventArgs).AsTask().Wait();
                return; // Exit the new instance immediately
            }

            // Otherwise, launch the app as normal
            Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
    }
}
