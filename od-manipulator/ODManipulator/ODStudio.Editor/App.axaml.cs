using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace ODStudio.Editor;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            var path = desktop.Args?.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Dispatcher.UIThread.Post(async () =>
                {
                    await window.OpenProjectFromPathAsync(path);
                    if (desktop.Args?.Contains("--resources", StringComparer.OrdinalIgnoreCase) == true)
                        window.OpenResourcesEditor();
                    else if (desktop.Args?.Contains("--scenes", StringComparer.OrdinalIgnoreCase) == true)
                        window.OpenSceneEditor();
                });
        }
        base.OnFrameworkInitializationCompleted();
    }
}
