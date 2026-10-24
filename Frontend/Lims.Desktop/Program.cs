using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Lims.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        StartupDiagnostics.Initialize();
        try
        {
            StartApplication();
        }
        catch (Exception error)
        {
            StartupDiagnostics.ReportFailure("Inicio de WinUI", error);
            Environment.ExitCode = 1;
        }
    }

    // Keep WinUI type loading inside the guarded call, after diagnostics is initialized.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void StartApplication()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
