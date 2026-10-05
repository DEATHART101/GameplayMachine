using Avalonia;

namespace ODStudio.Editor;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.FirstOrDefault() == "--od-runner-host")
        {
            Environment.ExitCode = OdRunnerHost.RunAsync(args.Skip(1).ToArray()).GetAwaiter().GetResult();
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
