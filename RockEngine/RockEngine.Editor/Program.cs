using NLog;

namespace RockEngine.Editor;

public static class Program
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private static EditorApplication _app;
    private static CancellationTokenSource _cts;

    [STAThread] 
    public static async Task Main(string[] args)
    {
        try
        {
            _logger.Info("Starting RockEngine Editor...");

            // Handle unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // Create cancellation token for graceful shutdown
            _cts = new CancellationTokenSource();

            // Handle console events for graceful shutdown
            Console.CancelKeyPress += OnConsoleCancelKeyPress;

            // Create and run the application
            _app = new EditorApplication();

            // Run the application asynchronously
            _app.Run();
        }
        catch (Exception ex)
        {
            _logger.Fatal(ex, "Fatal error starting editor");
            throw;
        }
    }

  

    private static async Task CleanupAsync()
    {
        _logger.Info("Cleaning up resources...");

        _cts?.Cancel();

        // Give the application time to shut down gracefully
        await Task.Delay(100);

        // Explicit disposal
        _app?.Dispose();

        LogManager.Shutdown();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        _logger.Fatal(ex, "Unhandled exception occurred. Is terminating: {0}", e.IsTerminating);

        if (ex != null)
        {
        
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger.Error(e.Exception, "Unobserved task exception");
        e.SetObserved(); // Mark as observed to prevent crash
    }

    private static void OnConsoleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        _logger.Info("Console cancel requested, shutting down...");
        e.Cancel = true; // Don't terminate immediately
        _cts?.Cancel();
        _app?.Dispose();
    }
}