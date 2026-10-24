using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Lims.Desktop;

internal static partial class StartupDiagnostics
{
    private static int _failureReported;
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LIMS", "Logs", $"startup-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");

    internal static void Initialize()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ReportFailure("Excepcion no controlada", args.ExceptionObject as Exception
                ?? new InvalidOperationException("Error no administrado."));
        Write($"Version={Assembly.GetExecutingAssembly().GetName().Version}; " +
            $"Windows={Environment.OSVersion.Version}; Arquitectura={RuntimeInformation.ProcessArchitecture}; " +
            $".NET={RuntimeInformation.FrameworkDescription}; EXE={Environment.ProcessPath}");
        Write("Entrada administrada iniciada");
    }

    internal static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)} {message}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            System.Diagnostics.Debug.WriteLine(error);
        }
    }

    internal static void ReportFailure(string stage, Exception error)
    {
        Write($"{stage}: HRESULT=0x{error.HResult:X8}{Environment.NewLine}{error}");
        if (Interlocked.Exchange(ref _failureReported, 1) != 0) return;
        _ = MessageBoxW(0,
            $"LIMS no pudo continuar.\n\n{stage}\n{error.GetType().Name}: 0x{error.HResult:X8}\n\n" +
            $"Registro de diagnostico (si Windows permite escribirlo):\n{LogPath}",
            "LIMS - Error", 0x10);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint window, string text, string caption, uint type);
}
