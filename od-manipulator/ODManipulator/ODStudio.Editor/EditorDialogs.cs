using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ODStudio.Generator;
using ODStudio.Model;

namespace ODStudio.Editor;

internal sealed record BuildGameplayMachineOutputOption(
    GameplayMachineBuildOutput Output,
    string Label,
    string Description)
{
    public bool RequiresPlatform => Output is GameplayMachineBuildOutput.StateSyncExportedGame or
        GameplayMachineBuildOutput.LockstepExportedGame;
    public override string ToString() => Label;
}

internal sealed record BuildGameplayMachineDialogResult(
    GameplayMachineBuildOutput Output,
    string OutputLabel,
    GameplayMachineExportPlatform? Platform);

internal enum ExternalProjectChangeChoice
{
    ReloadFromDisk,
    SaveWorkspace,
}

internal static class EditorDialogs
{
    public static Task<string?> PromptNameAsync(Window owner, string title, string label, string initial = "")
    {
        var editor = new TextBox { Text = initial };
        return ShowAsync(owner, title, BuildForm((label, editor)), editor, () =>
            string.IsNullOrWhiteSpace(editor.Text) ? null : editor.Text.Trim());
    }

    public static Task<CreateChoiceResult?> PromptNameAndChoiceAsync<T>(Window owner, string title,
        string nameLabel, IReadOnlyList<T> choices, string choiceLabel) where T : class
    {
        var name = new TextBox();
        var picker = new ComboBox { ItemsSource = choices, SelectedIndex = choices.Count > 0 ? 0 : -1 };
        return ShowAsync(owner, title, BuildForm((nameLabel, name), (choiceLabel, picker)), name, () =>
            string.IsNullOrWhiteSpace(name.Text) || picker.SelectedItem is not T choice
                ? null
                : new CreateChoiceResult(name.Text.Trim(), choice));
    }

    public static async Task<T?> PromptChoiceAsync<T>(Window owner, string title, string label,
        IReadOnlyList<T> choices) where T : struct, Enum
    {
        var picker = new ComboBox { ItemsSource = choices, SelectedIndex = choices.Count > 0 ? 0 : -1 };
        var dialog = DialogWindow(title, 470, 210);
        var ok = new Button { Content = "Build", MinWidth = 88 };
        var cancel = new Button { Content = "Cancel", MinWidth = 88 };
        T? result = null;
        ok.Click += (_, _) =>
        {
            if (picker.SelectedItem is not T choice)
                return;
            result = choice;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(18),
            Children =
            {
                BuildForm((label, picker)),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok },
                    [Grid.RowProperty] = 1,
                },
            },
        };
        dialog.Opened += (_, _) => picker.Focus();
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<T?> PromptObjectChoiceAsync<T>(Window owner, string title, string label,
        IReadOnlyList<T> choices) where T : class
    {
        var picker = new ComboBox { ItemsSource = choices, SelectedIndex = choices.Count > 0 ? 0 : -1 };
        var dialog = DialogWindow(title, 470, 210);
        var ok = new Button { Content = "Build", MinWidth = 88 };
        var cancel = new Button { Content = "Cancel", MinWidth = 88 };
        T? result = null;
        ok.Click += (_, _) =>
        {
            if (picker.SelectedItem is not T choice)
                return;
            result = choice;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(18),
            Children =
            {
                BuildForm((label, picker)),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok },
                    [Grid.RowProperty] = 1,
                },
            },
        };
        dialog.Opened += (_, _) => picker.Focus();
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task PromptBuildGameplayMachineAsync(
        Window owner,
        IReadOnlyList<BuildGameplayMachineOutputOption> choices,
        string? initialOutputDirectory,
        Func<BuildGameplayMachineDialogResult, string, IProgress<string>, CancellationToken,
            Task<GameplayMachineBuildResult>> buildAsync)
    {
        var outputPicker = new ComboBox
        {
            ItemsSource = choices,
            SelectedIndex = choices.Count > 0 ? 0 : -1,
        };
        var description = new TextBlock
        {
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 38,
        };
        var platformPicker = new ComboBox
        {
            ItemsSource = Enum.GetValues<GameplayMachineExportPlatform>(),
            SelectedItem = GameplayMachineExportPlatform.CSharp,
        };
        var platformPanel = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Platform", Foreground = Brushes.Gray },
                platformPicker,
            },
        };
        void RefreshSelection()
        {
            BuildGameplayMachineOutputOption? selected = outputPicker.SelectedItem as BuildGameplayMachineOutputOption;
            description.Text = selected?.Description ?? string.Empty;
            platformPanel.IsVisible = selected?.RequiresPlatform == true;
        }
        outputPicker.SelectionChanged += (_, _) => RefreshSelection();
        RefreshSelection();

        var outputDirectory = new TextBox
        {
            Text = initialOutputDirectory ?? string.Empty,
            PlaceholderText = "Build output directory",
        };
        var browse = new Button { Content = "...", Width = 34 };
        ToolTip.SetTip(browse, "Choose output directory");
        var outputDirectoryRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        outputDirectoryRow.Children.Add(outputDirectory);
        Grid.SetColumn(browse, 1);
        outputDirectoryRow.Children.Add(browse);

        var dialog = DialogWindow("Build GameplayMachine", 720, 620);
        dialog.CanResize = true;
        var build = new Button { Content = "Build", MinWidth = 88 };
        var cancel = new Button { Content = "Cancel", MinWidth = 88 };
        var status = new TextBlock { Text = "Ready to build", Foreground = Brushes.Gray };
        var progressBar = new ProgressBar { Height = 4, IsVisible = false };
        var log = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
        };
        ScrollViewer.SetVerticalScrollBarVisibility(log,
            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(log,
            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        var logBuffer = new System.Text.StringBuilder();
        CancellationTokenSource? cancellation = null;
        bool running = false;

        void AppendLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            logBuffer.AppendLine(message);
            log.Text = logBuffer.ToString();
            log.CaretIndex = log.Text.Length;
        }

        void SetRunning(bool value)
        {
            running = value;
            outputPicker.IsEnabled = !value;
            platformPicker.IsEnabled = !value;
            outputDirectory.IsEnabled = !value;
            browse.IsEnabled = !value;
            build.IsEnabled = !value;
            progressBar.IsVisible = value;
            progressBar.IsIndeterminate = value;
            cancel.Content = value ? "Cancel" : "Close";
            cancel.IsEnabled = true;
        }

        browse.Click += async (_, _) =>
        {
            IStorageFolder? suggested = null;
            if (!string.IsNullOrWhiteSpace(outputDirectory.Text) && Directory.Exists(outputDirectory.Text))
                suggested = await dialog.StorageProvider.TryGetFolderFromPathAsync(outputDirectory.Text);
            IReadOnlyList<IStorageFolder> folders = await dialog.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Choose build output directory",
                    AllowMultiple = false,
                    SuggestedStartLocation = suggested,
                });
            string? path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
                outputDirectory.Text = path;
        };

        build.Click += async (_, _) =>
        {
            if (running || outputPicker.SelectedItem is not BuildGameplayMachineOutputOption selected)
                return;
            GameplayMachineExportPlatform? platform = selected.RequiresPlatform &&
                                                      platformPicker.SelectedItem is GameplayMachineExportPlatform value
                ? value
                : null;
            if (selected.RequiresPlatform && platform is null)
                return;
            string destination = outputDirectory.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(destination))
            {
                status.Text = "Choose an output directory before building.";
                status.Foreground = Brushes.OrangeRed;
                return;
            }
            try
            {
                destination = Path.GetFullPath(destination);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                status.Text = "The output directory is invalid.";
                status.Foreground = Brushes.OrangeRed;
                return;
            }

            logBuffer.Clear();
            log.Text = string.Empty;
            status.Text = $"Building {selected.Label}...";
            status.Foreground = Brushes.White;
            SetRunning(true);
            cancellation = new CancellationTokenSource();
            var selection = new BuildGameplayMachineDialogResult(selected.Output, selected.Label, platform);
            var reporter = new Progress<string>(AppendLog);
            AppendLog($"Build started: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
            AppendLog($"Output type: {selected.Label}");
            AppendLog($"Destination: {destination}");
            try
            {
                GameplayMachineBuildResult result = await buildAsync(
                    selection, destination, reporter, cancellation.Token);
                foreach (OdIssue issue in result.Issues)
                    AppendLog($"[{issue.Severity}] {issue.Code}: {issue.Message}");
                if (result.Succeeded)
                {
                    AppendLog($"Build succeeded. {result.Files.Count} files written.");
                    foreach (string file in result.Files)
                        AppendLog($"  {file}");
                    status.Text = $"Build succeeded: {result.Files.Count} files";
                    status.Foreground = Brushes.LightGreen;
                }
                else
                {
                    int errors = result.Issues.Count(issue => issue.Severity == OdIssueSeverity.Error);
                    status.Text = $"Build failed: {errors} error(s)";
                    status.Foreground = Brushes.OrangeRed;
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog("Build cancelled.");
                status.Text = "Build cancelled";
                status.Foreground = Brushes.Gray;
            }
            catch (Exception exception)
            {
                AppendLog(exception.ToString());
                status.Text = "Build failed";
                status.Foreground = Brushes.OrangeRed;
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                SetRunning(false);
            }
        };
        cancel.Click += (_, _) =>
        {
            if (running)
            {
                status.Text = "Cancelling build...";
                cancel.IsEnabled = false;
                cancellation?.Cancel();
                return;
            }
            dialog.Close();
        };
        dialog.Closing += (_, args) =>
        {
            if (!running)
                return;
            args.Cancel = true;
            status.Text = "Cancelling build...";
            cancel.IsEnabled = false;
            cancellation?.Cancel();
        };

        var fields = new StackPanel
        {
            Spacing = 7,
            Children =
            {
                new TextBlock { Text = "Output Type", Foreground = Brushes.Gray },
                outputPicker,
                description,
                platformPanel,
                new TextBlock { Text = "Output Directory", Foreground = Brushes.Gray },
                outputDirectoryRow,
            },
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, build },
            [Grid.RowProperty] = 1,
        };
        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(18),
            RowSpacing = 10,
            Children =
            {
                fields,
                new StackPanel
                {
                    Spacing = 6,
                    Children = { status, progressBar },
                    [Grid.RowProperty] = 1,
                },
                log,
                buttons,
            },
        };
        Grid.SetRow(log, 2);
        Grid.SetRow(buttons, 3);
        dialog.Opened += (_, _) => outputPicker.Focus();
        await dialog.ShowDialog(owner);
    }

    public static async Task<bool> ConfirmAsync(Window owner, string title, string message)
    {
        var dialog = DialogWindow(title, 420, 180);
        var yes = new Button { Content = "Delete", MinWidth = 88 };
        var no = new Button { Content = "Cancel", MinWidth = 88 };
        var result = false;
        yes.Click += (_, _) => { result = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();
        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(18),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { no, yes },
                    [Grid.RowProperty] = 1,
                },
            },
        };
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<ExternalProjectChangeChoice> PromptExternalProjectChangeAsync(Window owner)
    {
        var dialog = DialogWindow("Project changed on disk", 520, 220);
        var reload = new Button { Content = "Reload from Disk", MinWidth = 120 };
        var overwrite = new Button { Content = "Save Workspace", MinWidth = 120 };
        ExternalProjectChangeChoice? result = null;
        reload.Click += (_, _) =>
        {
            result = ExternalProjectChangeChoice.ReloadFromDisk;
            dialog.Close();
        };
        overwrite.Click += (_, _) =>
        {
            result = ExternalProjectChangeChoice.SaveWorkspace;
            dialog.Close();
        };
        dialog.Closing += (_, eventArgs) =>
        {
            if (result is null)
                eventArgs.Cancel = true;
        };
        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(18),
            Children =
            {
                new TextBlock
                {
                    Text = "Files in this project changed outside GameplayMachine Editor. " +
                           "Reloading discards unsaved editor changes. Saving writes the current workspace " +
                           "over the files on disk.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { reload, overwrite },
                    [Grid.RowProperty] = 1,
                },
            },
        };
        await dialog.ShowDialog(owner);
        return result!.Value;
    }

    private static async Task<T?> ShowAsync<T>(Window owner, string title, Control content,
        Control focus, Func<T?> getResult)
    {
        var dialog = DialogWindow(title, 470, 245);
        var ok = new Button { Content = "Create", MinWidth = 88 };
        var cancel = new Button { Content = "Cancel", MinWidth = 88 };
        T? result = default;
        ok.Click += (_, _) =>
        {
            result = getResult();
            if (result is not null)
                dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(18) };
        root.Children.Add(content);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, ok },
        };
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        dialog.Content = root;
        dialog.Opened += (_, _) => focus.Focus();
        await dialog.ShowDialog(owner);
        return result;
    }

    private static Control BuildForm(params (string Label, Control Editor)[] rows)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach ((string label, Control editor) in rows)
        {
            panel.Children.Add(new TextBlock { Text = label, Foreground = Avalonia.Media.Brushes.Gray });
            panel.Children.Add(editor);
        }
        return panel;
    }

    private static Window DialogWindow(string title, double width, double height) => new()
    {
        Title = title,
        Width = width,
        Height = height,
        MinWidth = Math.Min(width, 320),
        MinHeight = Math.Min(height, 160),
        CanResize = true,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
    };
}
