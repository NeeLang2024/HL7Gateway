namespace HL7Gateway.Service;

public static class StartupDiagnostics
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Write("Unhandled exception", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);

            var line = exception is null
                ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\r\n"
                : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}: {exception}\r\n";

            File.AppendAllText(Path.Combine(logDir, "startup-diagnostic.log"), line);
        }
        catch
        {
            // Diagnostics must not become another startup failure.
        }
    }
}
