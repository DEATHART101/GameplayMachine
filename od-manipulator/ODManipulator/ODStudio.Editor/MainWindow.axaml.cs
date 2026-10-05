using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ODStudio.Model;
using ODStudio.Package;
using ODStudio.Generator;
using ODStudio.Export;
using ODStudio.Export.Unity;

namespace ODStudio.Editor;

public sealed partial class MainWindow : Window
{
    private const int MaximumVisibleGameLogEntries = 5000;
    private readonly ObservableCollection<ExplorerItem> _explorerItems = new();
    private readonly ObservableCollection<IssueItem> _issueItems = new();
    private readonly ObservableCollection<HistoryItem> _historyItems = new();
    private readonly ObservableCollection<GameLogItem> _gameLogItems = new();
    private readonly ObservableCollection<PackageReferenceItem> _packageReferenceItems = new();
    private readonly ObservableCollection<GameplayMachineFunctionItem> _gameplayMachineFunctionItems = new();
    private readonly Dictionary<Guid, OdProject> _loadedReferenceProjects = new();
    private IReadOnlyList<OdIssue> _packageGraphIssues = Array.Empty<OdIssue>();
    private int _referenceLoadVersion;
    private readonly Dictionary<Guid, Border> _fieldRows = new();
    private readonly SemaphoreSlim _editorStateSaveGate = new(1, 1);
    private readonly EditorPreferences _preferences = EditorPreferences.Load();
    private readonly DispatcherTimer _projectDiskChangeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private OdProjectSession? _session;
    private string? _projectDirectory;
    private OdProjectDiskSnapshot? _projectDiskSnapshot;
    private OdEntity? _selectedEntity;
    private InheritedFieldSelection? _inheritedFieldSelection;
    private OdProject? _browsedProject;
    private bool _dirty;
    private bool _allowClose;
    private bool _closePromptOpen;
    private bool _projectDiskOperation;
    private bool _externalChangePromptOpen;
    private ResourcesEditorWindow? _resourcesEditorWindow;
    private SceneEditorWindow? _sceneEditorWindow;
    private GameplayDebuggerWindow? _debuggerWindow;
    private Process? _runnerProcess;

    public MainWindow()
    {
        InitializeComponent();
        EditorWindowChrome.ConfigureWindow(this);
        ExplorerList.ItemsSource = _explorerItems;
        IssuesList.ItemsSource = _issueItems;
        HistoryList.ItemsSource = _historyItems;
        GameLogList.ItemsSource = _gameLogItems;
        PackageReferencesList.ItemsSource = _packageReferenceItems;
        GameplayMachineFunctionsList.ItemsSource = _gameplayMachineFunctionItems;
        RunnerModeCombo.ItemsSource = Enum.GetValues<RunnerMode>();
        RunnerModeCombo.SelectedItem = RunnerMode.Offline;
        ClearGameLogOnPlayToggle.IsChecked = _preferences.ClearGameLogOnPlay;
        WireCommands();
        RefreshCommandState();
        _projectDiskChangeTimer.Tick += async (_, _) => await CheckForExternalProjectChangesAsync();
        _projectDiskChangeTimer.Start();
        Closing += OnWindowClosing;
        Closed += (_, _) =>
        {
            _projectDiskChangeTimer.Stop();
            _resourcesEditorWindow?.Close();
            _sceneEditorWindow?.Close();
            _debuggerWindow?.Close();
            StopRunnerProcess();
        };
    }

    private void WireCommands()
    {
        NewMenuItem.Click += async (_, _) => await NewProjectAsync();
        OpenMenuItem.Click += async (_, _) => await OpenProjectAsync();
        SaveMenuItem.Click += async (_, _) => await SaveProjectAsync();
        ExitMenuItem.Click += (_, _) => Close();
        BuildMenuItem.Click += async (_, _) => await BuildPackageAsync();
        ValidateMenuItem.Click += (_, _) => ValidateProject(true);
        RenameMenuItem.Click += async (_, _) => await RenameSelectedEntityAsync();
        DeleteMenuItem.Click += (_, _) => DeleteSelectedEntity();
        ProjectSettingsMenuItem.Click += (_, _) => SelectProject();
        ResourcesEditorMenuItem.Click += (_, _) => OpenResourcesEditor();
        SceneEditorMenuItem.Click += (_, _) => OpenSceneEditor();
        DebuggerMenuItem.Click += (_, _) => OpenDebugger();
        RunButton.Click += async (_, _) => await RunProjectAsync(false);
        DebugButton.Click += async (_, _) => await RunProjectAsync(true);
        StopRunnerButton.Click += (_, _) =>
        {
            StopRunnerProcess();
            SetStatus("Runner stopped");
        };
        ClearGameLogButton.Click += (_, _) => ClearGameLog();
        ClearGameLogOnPlayToggle.Click += async (_, _) =>
        {
            _preferences.ClearGameLogOnPlay = ClearGameLogOnPlayToggle.IsChecked == true;
            await SavePreferencesAsync();
        };
        RunnerModeCombo.SelectionChanged += (_, _) => RefreshRunnerConfigurationState();
        UndoMenuItem.Click += (_, _) => _session?.Undo();
        RedoMenuItem.Click += (_, _) => _session?.Redo();
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => ToggleMaximize();
        CloseButton.Click += (_, _) => Close();
        this.FindControl<MenuItem>("AddOdMenu")!.Click += async (_, _) => await AddExplorerItemAsync(ExplorerAddKind.Od);
        this.FindControl<MenuItem>("AddClassMenu")!.Click += async (_, _) => await AddExplorerItemAsync(ExplorerAddKind.Class);
        this.FindControl<MenuItem>("AddStructMenu")!.Click += async (_, _) => await AddExplorerItemAsync(ExplorerAddKind.Struct);
        this.FindControl<MenuItem>("AddResourceMenu")!.Click += async (_, _) => await AddExplorerItemAsync(ExplorerAddKind.Resource);
        this.FindControl<MenuItem>("AddSwitchStructMenu")!.Click += async (_, _) => await AddExplorerItemAsync(ExplorerAddKind.SwitchStruct);
        this.FindControl<MenuItem>("AddEnumMenu")!.Click += async (_, _) => await AddExplorerItemAsync(ExplorerAddKind.Enum);
        this.FindControl<MenuItem>("AddInterfaceMenu")!.Click += async (_, _) => await AddExplorerItemAsync(ExplorerAddKind.Interface);
        this.FindControl<MenuItem>("AddPlayerInputMenu")!.Click += async (_, _) => await AddExplorerItemAsync(ExplorerAddKind.PlayerInput);
        this.FindControl<MenuItem>("AddEventMenu")!.Click += async (_, _) => await AddExplorerItemAsync(ExplorerAddKind.Event);
        SearchBox.TextChanged += (_, _) => RefreshExplorer();
        AddPackageReferenceButton.Click += async (_, _) => await AddPackageReferenceAsync();
        RemovePackageReferenceButton.Click += (_, _) => RemoveSelectedPackageReference();
        PackageReferencesList.SelectionChanged += (_, _) =>
            RemovePackageReferenceButton.IsEnabled = _browsedProject is null &&
                                                     PackageReferencesList.SelectedItem is PackageReferenceItem;
        PackageReferencesList.DoubleTapped += (_, _) => BrowseSelectedPackageReference();
        ReturnToProjectButton.Click += (_, _) => BrowseProject();
        ExplorerList.SelectionChanged += (_, _) =>
        {
            if (ExplorerList.SelectedItem is ExplorerItem { Entity: not null } item)
                SelectEntity(item.Entity);
        };
        IssuesList.DoubleTapped += (_, _) =>
        {
            if (_session is not null && IssuesList.SelectedItem is IssueItem { TargetId: { } id })
                SelectEntity(FindEntityAcrossPackages(id));
        };
        HistoryList.DoubleTapped += (_, _) =>
        {
            if (_session is not null && HistoryList.SelectedItem is HistoryItem { TargetId: { } id })
                SelectEntity(_session.Project.FindEntity(id));
        };
        KeyDown += OnWindowKeyDown;
        RefreshRunnerConfigurationState();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (eventArgs.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;
        if (eventArgs.ClickCount == 2)
            ToggleMaximize();
        else
            BeginMoveDrag(eventArgs);
        eventArgs.Handled = true;
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeButton.Content = WindowState == WindowState.Maximized
            ? CaptionIcon("M7 4 L20 4 L20 6 L7 6 Z M18 6 L20 6 L20 17 L18 17 Z M4 7 L17 7 L17 9 L4 9 Z M4 18 L17 18 L17 20 L4 20 Z M4 9 L6 9 L6 18 L4 18 Z M15 9 L17 9 L17 18 L15 18 Z")
            : CaptionIcon("M5 5 L19 5 L19 7 L5 7 Z M5 17 L19 17 L19 19 L5 19 Z M5 7 L7 7 L7 17 L5 17 Z M17 7 L19 7 L19 17 L17 17 Z");
        ToolTip.SetTip(MaximizeButton, WindowState == WindowState.Maximized ? "Restore" : "Maximize");
    }

    private static PathIcon CaptionIcon(string geometry) =>
        new() { Data = StreamGeometry.Parse(geometry), Width = 14, Height = 14 };

    private async Task NewProjectAsync()
    {
        if (!await ConfirmDiscardAsync())
            return;
        var path = await PickFolderAsync("Choose a directory for the new OD project");
        if (path is null)
            return;
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var project = OdProjectStore.Create(name);
        await OdProjectStore.SaveAsync(path, project);
        AttachProject(project, path, false);
        SetStatus($"Created {project.Name}");
    }

    private async Task OpenProjectAsync()
    {
        if (!await ConfirmDiscardAsync())
            return;
        var path = await PickFolderAsync("Open OD project directory", _preferences.LastOpenProjectDirectory);
        if (path is null)
            return;
        _preferences.LastOpenProjectDirectory = path;
        await SavePreferencesAsync();
        await OpenProjectFromPathAsync(path);
    }

    public async Task OpenProjectFromPathAsync(string path)
    {
        try
        {
            var project = await OdProjectStore.LoadAsync(path);
            AttachProject(project, path, false);
            SetStatus($"Opened {project.Name}");
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("Open failed", exception.Message);
        }
    }

    private async Task SaveProjectAsync()
    {
        if (_session is null || _projectDirectory is null)
            return;
        if (await ResolveExternalProjectChangesIfNeededAsync())
            return;
        try
        {
            _projectDiskOperation = true;
            OdProjectStore.RefreshCodeFilesFromDisk(_projectDirectory, _session.Project);
            OdProjectCodeManager.EnsureCodeFiles(_session.Project);
            await OdProjectStore.SaveAsync(_projectDirectory, _session.Project);
            RefreshProjectDiskSnapshot();
            SetDirty(false);
            SetStatus("Project saved");
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("Save failed", exception.Message);
        }
        finally
        {
            _projectDiskOperation = false;
        }
    }

    private async Task CheckForExternalProjectChangesAsync()
    {
        await ResolveExternalProjectChangesIfNeededAsync();
    }

    private async Task<bool> ResolveExternalProjectChangesIfNeededAsync()
    {
        if (_session is null || _projectDirectory is null || _projectDiskOperation ||
            _externalChangePromptOpen || _allowClose)
            return false;

        OdProjectDiskSnapshot currentSnapshot;
        try
        {
            currentSnapshot = OdProjectDiskSnapshot.Capture(_projectDirectory);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (_projectDiskSnapshot is null)
        {
            _projectDiskSnapshot = currentSnapshot;
            return false;
        }
        if (_projectDiskSnapshot.Value == currentSnapshot)
            return false;

        _externalChangePromptOpen = true;
        try
        {
            ExternalProjectChangeChoice choice = await EditorDialogs.PromptExternalProjectChangeAsync(this);
            _projectDiskOperation = true;
            if (choice == ExternalProjectChangeChoice.ReloadFromDisk)
            {
                OdProject project = await OdProjectStore.LoadAsync(_projectDirectory);
                string projectDirectory = _projectDirectory;
                AttachProject(project, projectDirectory, false);
                SetStatus("Reloaded project changes from disk");
            }
            else
            {
                OdProjectCodeManager.EnsureCodeFiles(_session.Project);
                await OdProjectStore.OverwriteAsync(_projectDirectory, _session.Project);
                RefreshProjectDiskSnapshot();
                SetDirty(false);
                SetStatus("Saved workspace over external disk changes");
            }
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("Could not resolve project changes", exception.Message);
        }
        finally
        {
            _projectDiskOperation = false;
            _externalChangePromptOpen = false;
        }
        return true;
    }

    private async Task SaveCodeFilesAndRefreshSnapshotAsync()
    {
        if (_session is null || _projectDirectory is null)
            return;
        _projectDiskOperation = true;
        try
        {
            await OdProjectStore.SaveCodeFilesAsync(
                Path.Combine(_projectDirectory, "code"), _session.Project.CodeFiles);
            RefreshProjectDiskSnapshot();
        }
        finally
        {
            _projectDiskOperation = false;
        }
    }

    private void RefreshProjectDiskSnapshot()
    {
        if (_projectDirectory is null)
        {
            _projectDiskSnapshot = null;
            return;
        }
        try
        {
            _projectDiskSnapshot = OdProjectDiskSnapshot.Capture(_projectDirectory);
        }
        catch (IOException)
        {
            _projectDiskSnapshot = null;
        }
        catch (UnauthorizedAccessException)
        {
            _projectDiskSnapshot = null;
        }
    }

    private async Task BuildPackageAsync()
    {
        if (_session is null)
            return;
        if (await ResolveExternalProjectChangesIfNeededAsync())
            return;
        ValidateProject(false);
        if (_issueItems.Any(item => item.Severity == nameof(OdIssueSeverity.Error)))
        {
            await ShowMessageAsync("Build blocked", "Fix validation errors before building the package.");
            return;
        }
        IReadOnlyList<BuildGameplayMachineOutputOption> outputs = GameplayMachineBuildPublisher
            .OutputsFor(_session.Project.Runtime.NetworkMode)
            .Select(item => new BuildGameplayMachineOutputOption(
                item,
                item switch
                {
                    GameplayMachineBuildOutput.StateSyncCommandLineGame or
                        GameplayMachineBuildOutput.LockstepCommandLineGame => "Command-line Game",
                    GameplayMachineBuildOutput.LockstepRelayServer => "Relay Server",
                    _ => "Exported Game",
                },
                item switch
                {
                    GameplayMachineBuildOutput.StateSyncCommandLineGame =>
                        "Standalone executable for offline, host, or dedicated-server startup.",
                    GameplayMachineBuildOutput.LockstepCommandLineGame =>
                        "Lockstep game with a bundled optional Relay Server, or connection to a remote Relay.",
                    GameplayMachineBuildOutput.LockstepRelayServer =>
                        "Headless server that orders player inputs and can record matches.",
                    GameplayMachineBuildOutput.LockstepExportedGame =>
                        "Generated game runtime with an optional bundled Relay Server for the selected platform.",
                    _ => "Generated runtime source for embedding in a game engine or C# host.",
                }))
            .ToArray();
        await EditorDialogs.PromptBuildGameplayMachineAsync(
            this,
            outputs,
            _preferences.LastBuildDirectory,
            async (selected, outputPath, progress, cancellationToken) =>
            {
                _preferences.LastBuildDirectory = outputPath;
                await SavePreferencesAsync();
                progress.Report("Preparing project code...");
                await PrepareProjectCodeAsync();
                IOdExportTarget? exportTarget = selected.Platform switch
                {
                    GameplayMachineExportPlatform.CSharp => new PlainCSharpExportTarget(),
                    GameplayMachineExportPlatform.Unity => new UnityExportTarget(),
                    GameplayMachineExportPlatform.Godot => new GodotExportTarget(),
                    null => null,
                    _ => throw new ArgumentOutOfRangeException(),
                };
                GameplayMachineBuildResult result = await GameplayMachineBuildPublisher.PublishAsync(
                    _session.Project, selected.Output, outputPath, AppContext.BaseDirectory,
                    exportTarget, cancellationToken, progress);
                RefreshIssues(result.Issues);
                if (result.Succeeded)
                    SetStatus($"Built {selected.OutputLabel}: {result.Files.Count} files in {outputPath}");
                return result;
            });
    }

    private async Task<string?> PickFolderAsync(string title, string? suggestedPath = null)
    {
        try
        {
            IStorageFolder? suggestedFolder = null;
            if (!string.IsNullOrWhiteSpace(suggestedPath) && Directory.Exists(suggestedPath))
                suggestedFolder = await StorageProvider.TryGetFolderFromPathAsync(suggestedPath);

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = suggestedFolder,
            });
            return folders.FirstOrDefault()?.TryGetLocalPath();
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("Folder selection failed", exception.Message);
            return null;
        }
    }

    private async Task SavePreferencesAsync()
    {
        try
        {
            await _preferences.SaveAsync();
        }
        catch (Exception exception)
        {
            SetStatus($"Could not save editor preferences: {exception.Message}");
        }
    }

    private void AttachProject(OdProject project, string projectDirectory, bool dirty)
    {
        _resourcesEditorWindow?.Close();
        _sceneEditorWindow?.Close();
        if (_session is not null)
            _session.ProjectChanged -= OnProjectChanged;
        project.SourceDirectory = projectDirectory;
        _session = new OdProjectSession(project);
        _session.ProjectChanged += OnProjectChanged;
        _projectDirectory = projectDirectory;
        RefreshProjectDiskSnapshot();
        _inheritedFieldSelection = null;
        _browsedProject = null;
        _loadedReferenceProjects.Clear();
        OdProject networkPackage = OdNetworkPackage.CreateProject(project.Runtime.NetworkMode);
        _loadedReferenceProjects[networkPackage.Id] = networkPackage;
        _packageGraphIssues = Array.Empty<OdIssue>();
        _selectedEntity = project.EditorState.SelectedEntityId is { } selectedId ? project.FindEntity(selectedId) : null;
        ProjectPathText.Text = projectDirectory;
        SetDirty(dirty);
        RebuildWorkspace();
        _ = ReloadPackageReferencesAsync();
    }

    private async Task ReloadPackageReferencesAsync()
    {
        if (_session is null)
            return;
        int loadVersion = ++_referenceLoadVersion;
        OdProject root = _session.Project;
        try
        {
            root.SourceDirectory = _projectDirectory ?? root.SourceDirectory;
            OdPackageGraph graph = await OdPackageGraph.LoadForProjectAsync(root);
            if (_session?.Project.Id != root.Id || loadVersion != _referenceLoadVersion)
                return;
            _loadedReferenceProjects.Clear();
            foreach (OdProject dependency in graph.Projects.Skip(1))
                _loadedReferenceProjects[dependency.Id] = dependency;
            _packageGraphIssues = graph.Issues;
            RefreshPackageReferences(graph.Issues);
            RefreshIssues(graph.Issues.Concat(OdProjectValidator.Validate(root, ReferencedDefinitions())).ToList());
            RefreshExplorer();
            ShowSelectedEditor();
        }
        catch (Exception exception)
        {
            SetStatus($"Could not load package references: {exception.Message}");
        }
    }

    private void RefreshPackageReferences(IReadOnlyList<OdIssue> issues)
    {
        _packageReferenceItems.Clear();
        if (_session is null)
            return;
        foreach (OdPackageReference packageReference in _session.Project.PackageReferences)
        {
            bool loaded = _loadedReferenceProjects.ContainsKey(packageReference.PackageId);
            OdIssue? issue = issues.FirstOrDefault(item => item.TargetId == packageReference.Id &&
                                                           item.Severity == OdIssueSeverity.Error);
            _packageReferenceItems.Add(new PackageReferenceItem(
                packageReference.Id,
                packageReference.PackageId,
                packageReference.Name,
                packageReference.PackagePath,
                issue?.Message ?? (loaded ? "Ready · double-click to browse" : "Not loaded"),
                issue is null && loaded ? Brushes.LightGreen : Brushes.IndianRed));
        }
    }

    private async Task AddPackageReferenceAsync()
    {
        if (_session is null || _projectDirectory is null)
            return;
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Reference OD package",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("OD Package") { Patterns = new[] { "*.odpkg" } } },
        });
        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null)
            return;
        try
        {
            OdProject package = await OdPackageCompiler.LoadAsync(path);
            if (package.Id == _session.Project.Id)
            {
                await ShowMessageAsync("Reference rejected", "A project cannot reference itself.");
                return;
            }
            if (_session.Project.PackageReferences.Any(item => item.PackageId == package.Id))
            {
                await ShowMessageAsync("Reference already exists", $"Package '{package.Name}' is already referenced.");
                return;
            }
            var packageReference = new OdPackageReference
            {
                Name = package.Name,
                PackageId = package.Id,
                PackagePath = Path.GetRelativePath(_projectDirectory, Path.GetFullPath(path)),
            };
            _session.Execute(OdChangeKind.Create, packageReference.Id, nameof(OdPackageReference),
                $"Reference package {package.Name}", project => project.PackageReferences.Add(packageReference));
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("Reference failed", exception.Message);
        }
    }

    private void RemoveSelectedPackageReference()
    {
        if (_session is null || PackageReferencesList.SelectedItem is not PackageReferenceItem selected)
            return;
        if (_browsedProject?.Id == selected.PackageId)
            BrowseProject();
        _session.Execute(OdChangeKind.Delete, selected.ReferenceId, nameof(OdPackageReference),
            $"Remove package reference {selected.Name}",
            project => project.PackageReferences.RemoveAll(item => item.Id == selected.ReferenceId));
    }

    private void BrowseSelectedPackageReference()
    {
        if (PackageReferencesList.SelectedItem is not PackageReferenceItem selected ||
            !_loadedReferenceProjects.TryGetValue(selected.PackageId, out OdProject? project))
            return;
        _browsedProject = project;
        _selectedEntity = null;
        _inheritedFieldSelection = null;
        NavigationTabs.SelectedItem = ExplorerTab;
        RefreshExplorer();
        ShowSelectedEditor();
        RefreshCommandState();
    }

    private void BrowseProject()
    {
        if (_session is null)
            return;
        _browsedProject = null;
        _selectedEntity = _session.Project.EditorState.SelectedEntityId is { } selectedId
            ? _session.Project.FindEntity(selectedId)
            : null;
        RefreshExplorer();
        ShowSelectedEditor();
        RefreshCommandState();
    }

    public void OpenResourcesEditor()
    {
        if (_session is null)
            return;
        if (_resourcesEditorWindow is not null)
        {
            _resourcesEditorWindow.Activate();
            return;
        }
        _resourcesEditorWindow = new ResourcesEditorWindow(_session, SaveProjectAsync, ReferencedDefinitions);
        _resourcesEditorWindow.Closed += (_, _) => _resourcesEditorWindow = null;
        _resourcesEditorWindow.Show();
    }

    public void OpenSceneEditor()
    {
        if (_session is null)
            return;
        if (_sceneEditorWindow is not null)
        {
            _sceneEditorWindow.Activate();
            return;
        }
        _sceneEditorWindow = new SceneEditorWindow(_session, SaveProjectAsync, ReferencedDefinitions);
        _sceneEditorWindow.Closed += (_, _) => _sceneEditorWindow = null;
        _sceneEditorWindow.Show();
    }

    public void OpenDebugger()
    {
        if (_debuggerWindow is not null)
        {
            _debuggerWindow.Activate();
            return;
        }
        _debuggerWindow = new GameplayDebuggerWindow();
        _debuggerWindow.Closed += (_, _) => _debuggerWindow = null;
        _debuggerWindow.Show();
    }

    private async Task RunProjectAsync(bool attachDebugger)
    {
        if (_session is null || _projectDirectory is null || IsRunnerRunning())
            return;
        if (await ResolveExternalProjectChangesIfNeededAsync())
            return;
        ValidateProject(false);
        if (_issueItems.Any(item => item.Severity == nameof(OdIssueSeverity.Error)))
        {
            await ShowMessageAsync("Run blocked", "Fix validation errors before running the OD project.");
            return;
        }

        if (RunnerModeCombo.SelectedItem is not RunnerMode mode ||
            RunnerPortInput.Value is not { } portValue ||
            RunnerRequestedSlotInput.Value is not { } requestedSlotValue ||
            RunnerPlayerCountInput.Value is not { } playerCountValue)
            return;
        string address = string.IsNullOrWhiteSpace(RunnerAddressTextBox.Text)
            ? "0.0.0.0"
            : RunnerAddressTextBox.Text.Trim();
        var options = new RunnerLaunchOptions(mode, address, decimal.ToInt32(portValue),
            decimal.ToInt32(requestedSlotValue),
            decimal.ToInt32(playerCountValue),
            _session.Project.Runtime.LockstepTickMode);
        if (ClearGameLogOnPlayToggle.IsChecked == true)
            ClearGameLog();
        OutputTabs.SelectedItem = GameLogTab;
        try
        {
            _debuggerWindow?.Close();
            StopRunnerProcess();
            RunButton.IsEnabled = false;
            DebugButton.IsEnabled = false;
            SetStatus("Building OD runtime...");
            OdProjectStore.RefreshCodeFilesFromDisk(_projectDirectory, _session.Project);
            OdProjectCodeManager.EnsureCodeFiles(_session.Project);
            await SaveCodeFilesAndRefreshSnapshotAsync();
            RunnerLaunchResult launch = await OdRunnerService.BuildAndStartAsync(
                _session.Project, options, AppendGameLog);
            _runnerProcess = launch.Process;
            launch.Process.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_runnerProcess, launch.Process))
                {
                    _runnerProcess = null;
                    RefreshRunnerRunningState();
                    SetStatus("Runner stopped");
                }
            });
            RefreshRunnerRunningState();
            if (attachDebugger)
            {
                OpenDebugger();
                GameplayDebuggerWindow debugger = _debuggerWindow!;
                debugger.OwnProcess(launch.Process);
                await debugger.ConnectAsync("127.0.0.1", launch.DebugPort, launch.DebugToken);
            }
            SetStatus($"Running {_session.Project.Name} ({options.Mode}{(attachDebugger ? ", debug attached" : string.Empty)})");
        }
        catch (Exception exception)
        {
            AppendGameLog(new RunnerOutputLine(DateTimeOffset.Now, RunnerOutputStream.Error, exception.Message));
            StopRunnerProcess();
            await ShowMessageAsync("Run failed", exception.Message);
            SetStatus("Run failed");
        }
        finally
        {
            RefreshRunnerRunningState();
        }
    }

    private void RefreshRunnerConfigurationState()
    {
        bool canConfigure = _session is not null && !IsRunnerRunning();
        bool host = RunnerModeCombo.SelectedItem is RunnerMode.AuthorityHost;
        bool client = RunnerModeCombo.SelectedItem is RunnerMode.ClientProxy;
        bool lockstepHost = host && _session?.Project.Runtime.NetworkMode == OdNetworkMode.Lockstep;
        bool lockstep = _session?.Project.Runtime.NetworkMode == OdNetworkMode.Lockstep;
        bool lockstepNetworkPeer = lockstep && (host || client);
        RunnerModeCombo.IsEnabled = canConfigure;
        RunnerAddressTextBox.IsEnabled = canConfigure && (host || client);
        RunnerPortInput.IsEnabled = canConfigure && (host || client);
        RunnerRequestedSlotLabel.IsVisible = lockstepNetworkPeer;
        RunnerRequestedSlotInput.IsVisible = lockstepNetworkPeer;
        RunnerRequestedSlotInput.IsEnabled = canConfigure && lockstepNetworkPeer;
        RunnerPlayerCountLabel.IsVisible = lockstepHost;
        RunnerPlayerCountInput.IsVisible = lockstepHost;
        RunnerPlayerCountInput.IsEnabled = canConfigure && lockstepHost;
        if (host && string.IsNullOrWhiteSpace(RunnerAddressTextBox.Text))
            RunnerAddressTextBox.Text = "0.0.0.0";
        else if (client && (string.IsNullOrWhiteSpace(RunnerAddressTextBox.Text) ||
                            RunnerAddressTextBox.Text.Trim() == "0.0.0.0"))
            RunnerAddressTextBox.Text = "127.0.0.1";

        RunnerAddressTextBox.PlaceholderText = client
            ? lockstep ? "Relay address" : "Authority address"
            : "Listen address";
        ToolTip.SetTip(RunnerAddressTextBox, client
            ? lockstep ? "Remote Lockstep Relay address" : "Remote Authority server address"
            : lockstep ? "Lockstep Relay listen address" : "Authority listen address");
        ToolTip.SetTip(RunnerPortInput, client
            ? lockstep ? "Remote Lockstep Relay port" : "Remote Authority gameplay port"
            : lockstep ? "Lockstep Relay listen port" : "Authority gameplay port");
    }

    private bool IsRunnerRunning() => _runnerProcess is { HasExited: false };

    private void AppendGameLog(RunnerOutputLine line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            GameLogItem item = GameLogItem.From(line);
            _gameLogItems.Add(item);
            while (_gameLogItems.Count > MaximumVisibleGameLogEntries)
                _gameLogItems.RemoveAt(0);
            GameLogList.ScrollIntoView(item);
        });
    }

    private void ClearGameLog()
    {
        _gameLogItems.Clear();
    }

    private void RefreshRunnerRunningState()
    {
        bool running = IsRunnerRunning();
        RunButton.IsVisible = !running;
        DebugButton.IsVisible = !running;
        StopRunnerButton.IsVisible = running;
        RunButton.IsEnabled = !running && _session is not null;
        DebugButton.IsEnabled = !running && _session is not null;
        RunnerFrame.IsVisible = running;
        RefreshRunnerConfigurationState();
    }

    private void StopRunnerProcess()
    {
        try
        {
            if (_runnerProcess is { HasExited: false })
                _runnerProcess.Kill(true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and termination.
        }
        finally
        {
            _runnerProcess = null;
            RefreshRunnerRunningState();
        }
    }

    private IEnumerable<OdDefinition> ReferencedDefinitions() =>
        _loadedReferenceProjects.Values.SelectMany(project => project.Definitions);

    private IEnumerable<OdDefinition> AllDefinitions() =>
        (_session is null ? Enumerable.Empty<OdDefinition>() : _session.Project.Definitions)
        .Concat(ReferencedDefinitions());

    private OdProject ConfigurationProject() => new()
    {
        Id = _session!.Project.Id,
        Name = _session.Project.Name,
        DefaultNamespace = _session.Project.DefaultNamespace,
        Runtime = _session.Project.Runtime,
        Definitions = AllDefinitions().Concat(OdNetworkPackage.CreateProject(_session.Project.Runtime.NetworkMode).Definitions)
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .ToList(),
        DataSets = _session.Project.DataSets,
        Scenes = _session.Project.Scenes,
    };

    private OdProject? FindOwningProject(Guid entityId)
    {
        if (_session?.Project.FindEntity(entityId) is not null)
            return _session.Project;
        return _loadedReferenceProjects.Values.FirstOrDefault(project => project.FindEntity(entityId) is not null);
    }

    private OdEntity? FindEntityAcrossPackages(Guid entityId) => FindOwningProject(entityId)?.FindEntity(entityId);

    private void OnProjectChanged()
    {
        SetDirty(true);
        Dispatcher.UIThread.Post(() =>
        {
            if (_session is null)
                return;
            if (_projectDirectory is not null)
                _session.Project.SourceDirectory = _projectDirectory;
            var selectedId = _inheritedFieldSelection?.FieldId ?? _selectedEntity?.Id ?? _session.Project.EditorState.SelectedEntityId;
            _selectedEntity = selectedId.HasValue ? _session.Project.FindEntity(selectedId.Value) : null;
            RebuildWorkspace();
            _ = ReloadPackageReferencesAsync();
        });
    }

    private void RebuildWorkspace()
    {
        RefreshExplorer();
        RefreshGameplayMachineFunctions();
        RefreshIssues(_packageGraphIssues.Concat(
            OdProjectValidator.Validate(_session!.Project, ReferencedDefinitions())).ToList());
        RefreshHistory();
        ShowSelectedEditor();
        RefreshCommandState();
    }

    private void RefreshExplorer()
    {
        _explorerItems.Clear();
        if (_session is null)
            return;
        var filter = SearchBox.Text?.Trim();
        bool Match(OdEntity entity) => string.IsNullOrEmpty(filter) || entity.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var project = _browsedProject ?? _session.Project;
        AddExplorerGroup("ODS", "◆", ExplorerAddKind.Od, null, 0);
        foreach (var scope in project.Ods.OrderBy(item => item.Name))
        {
            _explorerItems.Add(new ExplorerItem
            {
                DisplayName = scope.Name,
                Icon = "O",
                Entity = scope,
                OwnerOdId = scope.Id,
                Indent = 1,
                ContextMenu = BuildExplorerContextMenu(scope),
            });
            AddDefinitionGroup<OdClassDefinition>(scope, "CLASSES", ExplorerAddKind.Class);
            AddDefinitionGroup<OdStructDefinition>(scope, "STRUCTS", ExplorerAddKind.Struct);
            AddDefinitionGroup<OdResourceDefinition>(scope, "RESOURCES", ExplorerAddKind.Resource);
            AddDefinitionGroup<OdSwitchStructDefinition>(scope, "SWITCH STRUCTS", ExplorerAddKind.SwitchStruct);
            AddDefinitionGroup<OdEnumDefinition>(scope, "ENUMS", ExplorerAddKind.Enum);
            AddDefinitionGroup<OdInterfaceDefinition>(scope, "INTERFACES", ExplorerAddKind.Interface,
                item => item.InterfaceType == OdInterfaceType.Gameplay);
            AddDefinitionGroup<OdInterfaceDefinition>(scope, "PLAYER INPUTS", ExplorerAddKind.PlayerInput,
                item => item.InterfaceType == OdInterfaceType.PlayerInput);
            AddDefinitionGroup<OdEventDefinition>(scope, "EVENTS", ExplorerAddKind.Event);
        }

        void AddDefinitionGroup<TDefinition>(OdScope scope, string name, ExplorerAddKind addKind,
            Func<TDefinition, bool>? include = null)
            where TDefinition : OdDefinition
        {
            AddExplorerGroup(name, "◆", addKind, scope.Id, 2);
            foreach (var definition in project.Definitions.OfType<TDefinition>()
                         .Where(item => item.Namespace == scope.Namespace && Match(item) &&
                                        (include is null || include(item)))
                         .OrderBy(item => item.Name))
            {
                _explorerItems.Add(new ExplorerItem
                {
                    DisplayName = definition.Name,
                    Icon = DefinitionIcon(definition.Kind),
                    Entity = definition,
                    OwnerOdId = scope.Id,
                    Indent = 3,
                    CanEditCode = _browsedProject is null && definition is OdInterfaceDefinition
                        { Abstract: false },
                    ContextMenu = BuildExplorerContextMenu(definition),
                });
            }
        }
    }

    private ContextMenu? BuildExplorerContextMenu(OdEntity entity)
    {
        if (_browsedProject is not null)
            return null;
        var items = new List<object>();
        if (entity is OdInterfaceDefinition { Abstract: false } interfaceDefinition)
        {
            var editCode = new MenuItem { Header = "Edit Code" };
            editCode.Click += async (_, _) => await EditEntityCodeAsync(interfaceDefinition.Id);
            items.Add(editCode);
            items.Add(new Separator());
        }
        var delete = new MenuItem { Header = "Delete" };
        delete.Click += async (_, _) =>
        {
            string kind = EntityKindLabel(entity);
            if (await ShowConfirmAsync($"Delete {kind.ToLowerInvariant()}",
                    $"Delete {kind.ToLowerInvariant()} '{entity.Name}'? You can undo this action.", "Delete"))
                DeleteEntity(entity);
        };
        items.Add(delete);
        return new ContextMenu { ItemsSource = items };
    }

    private void RefreshGameplayMachineFunctions()
    {
        _gameplayMachineFunctionItems.Clear();
        if (_session is null)
            return;
        foreach (OdGameplayMachineFunctionDescriptor descriptor in OdGameplayMachineFunctionCatalog.All)
        {
            if (!OdGameplayMachineFunctionCatalog.IsAvailable(_session.Project, descriptor))
                continue;
            bool implemented = OdProjectCodeManager.HasGameplayMachineFunctionFile(
                _session.Project, descriptor.Function);
            _gameplayMachineFunctionItems.Add(new GameplayMachineFunctionItem(
                descriptor.Function,
                descriptor.Name,
                OdGameplayMachineFunctionCatalog.Signature(_session.Project, descriptor),
                descriptor.Description,
                implemented ? "Implemented" : "Not implemented"));
        }
    }

    private async void OnGameplayMachineFunctionEditClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { DataContext: GameplayMachineFunctionItem item })
            await EditGameplayMachineFunctionAsync(item.Function);
    }

    private void AddExplorerGroup(string name, string icon, ExplorerAddKind addKind, Guid? ownerOdId, int indent) =>
        _explorerItems.Add(new ExplorerItem
        {
            DisplayName = name,
            Icon = icon,
            AddKind = _browsedProject is null ? addKind : null,
            OwnerOdId = ownerOdId,
            Indent = indent,
        });

    private void RefreshIssues(IReadOnlyList<OdIssue> issues)
    {
        _issueItems.Clear();
        foreach (var issue in issues)
            _issueItems.Add(IssueItem.From(issue));
    }

    private void RefreshHistory()
    {
        _historyItems.Clear();
        if (_session is null)
            return;
        foreach (var record in _session.Project.History.OrderByDescending(item => item.Sequence))
            _historyItems.Add(HistoryItem.From(record));
    }

    private void ValidateProject(bool updateStatus)
    {
        if (_session is null)
            return;
        var issues = _packageGraphIssues.Concat(
            OdProjectValidator.Validate(_session.Project, ReferencedDefinitions())).ToList();
        RefreshIssues(issues);
        if (updateStatus)
            SetStatus($"Validation: {issues.Count(item => item.Severity == OdIssueSeverity.Error)} errors, " +
                      $"{issues.Count(item => item.Severity == OdIssueSeverity.Warning)} warnings");
    }

    private void SelectEntity(OdEntity? entity)
    {
        if (entity is null || _session is null)
            return;
        _inheritedFieldSelection = null;
        _selectedEntity = entity;
        OdProject? owner = FindOwningProject(entity.Id);
        if (owner is not null)
            _browsedProject = owner.Id == _session.Project.Id ? null : owner;
        if (_browsedProject is null)
            _session.Project.EditorState.SelectedEntityId = entity.Id;
        ShowSelectedEditor();
        RefreshCommandState();
    }

    private void SelectProject()
    {
        if (_session is null)
            return;
        _inheritedFieldSelection = null;
        _browsedProject = null;
        _selectedEntity = null;
        _session.Project.EditorState.SelectedEntityId = null;
        ExplorerList.SelectedItem = null;
        ShowSelectedEditor();
        RefreshCommandState();
    }

    private void ShowSelectedEditor()
    {
        if (_session is null)
        {
            DocumentTitle.Text = "No project open";
            EditorHost.Content = EmptyMessage("Create or open an OD project.");
            InspectorHost.Content = null;
            return;
        }
        bool readOnly = _browsedProject is not null;
        EditorHost.IsEnabled = true;
        InspectorHost.IsEnabled = true;
        BrowseTitleText.Text = readOnly ? $"READ ONLY · {_browsedProject!.Name}" : "PROJECT";
        ReturnToProjectButton.IsVisible = readOnly;
        if (_selectedEntity is null)
        {
            DocumentTitle.Text = (_browsedProject ?? _session.Project).Name;
            EditorHost.Content = BuildProjectOverview(_browsedProject ?? _session.Project);
            InspectorHost.Content = readOnly ? BuildReadOnlyPackageInspector(_browsedProject!) : BuildProjectInspector();
            return;
        }

        if (_inheritedFieldSelection is { } inheritedSelection &&
            _session.Project.FindEntity(inheritedSelection.OwnerClassId) is OdClassDefinition ownerClass &&
            FindEntityAcrossPackages(inheritedSelection.SourceClassId) is OdClassDefinition sourceClass &&
            FindEntityAcrossPackages(inheritedSelection.FieldId) is OdFieldDefinition inheritedField)
        {
            DocumentTitle.Text = ownerClass.Name;
            EditorHost.Content = BuildDefinitionEditor(ownerClass);
            InspectorHost.Content = BuildInheritedFieldInspector(inheritedField, sourceClass);
            return;
        }

        _inheritedFieldSelection = null;

        DocumentTitle.Text = _selectedEntity.Name;
        EditorHost.Content = _selectedEntity switch
        {
            OdScope scope => BuildOdOverview(scope),
            OdDefinition definition => BuildDefinitionEditor(definition),
            OdDataSet dataSet => BuildDataSetEditor(dataSet),
            _ => BuildOwningEntityEditor(_selectedEntity),
        };
        InspectorHost.Content = readOnly ? BuildReadOnlyEntityInspector(_selectedEntity) : BuildEntityInspector(_selectedEntity);
    }

    private Control BuildProjectOverview(OdProject? displayedProject = null)
    {
        var project = displayedProject ?? _session!.Project;
        return new StackPanel
        {
            Margin = new Thickness(28),
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                new TextBlock { Text = project.Name, FontSize = 28, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = project.Description, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 24), TextWrapping = TextWrapping.Wrap },
                StatBlock("ODs", project.Ods.Count),
                StatBlock("Schemas", project.Definitions.Count),
                StatBlock("Static data sets", project.DataSets.Count),
            },
        };
    }

    private Control BuildProjectInspector()
    {
        var panel = InspectorPanel("Project");
        AddTextEditor(panel, "Name", _session!.Project.Name, RenameProject);
        AddTextEditor(panel, "Description", _session.Project.Description,
            value => UpdateProject("Update project description", project => project.Description = value), true);
        OdRuntimeSettings runtime = _session.Project.Runtime;

        panel.Children.Add(SettingsGroupTitle("Simulation"));
        bool fixedRateTick;
        if (runtime.NetworkMode == OdNetworkMode.Lockstep)
        {
            AddEnumEditor(panel, "Tick Mode", runtime.LockstepTickMode,
                value => UpdateProject("Change Lockstep tick mode",
                    project => project.Runtime.LockstepTickMode = value));
            fixedRateTick = runtime.LockstepTickMode == OdLockstepTickMode.FixedRate;
        }
        else
        {
            AddEnumEditor(panel, "Tick Mode", runtime.StateSyncTickMode,
                value => UpdateProject("Change State Sync tick mode",
                    project => project.Runtime.StateSyncTickMode = value));
            fixedRateTick = runtime.StateSyncTickMode == OdStateSyncTickMode.FixedRate;
        }
        if (fixedRateTick)
        {
            AddIntEditor(panel, "Tick Interval (milliseconds)", runtime.TickIntervalMilliseconds, 1,
                value => UpdateProject("Change tick interval",
                    project => project.Runtime.TickIntervalMilliseconds = value));
        }
        if (runtime.NetworkMode == OdNetworkMode.Lockstep)
        {
            panel.Children.Add(new TextBlock
            {
                Text = runtime.LockstepTickMode == OdLockstepTickMode.InputDriven
                    ? "Input Driven advances the simulation only when at least one PlayerInput is pending. Input delay is fixed at zero."
                    : "Fixed Rate advances the simulation at the configured tick interval, including ticks without PlayerInput.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 4, 0, 12),
            });
        }

        panel.Children.Add(SettingsGroupTitle("Networking"));
        AddNetworkModeEditor(panel, runtime.NetworkMode);
        if (runtime.NetworkMode == OdNetworkMode.Lockstep)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Lockstep replaces Single, Double, and Decimal with deterministic Fixed64. Transform, Actor/Pawn inheritance, routines, multicast events, and partition interest are unavailable.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 12),
            });
            return panel;
        }

        panel.Children.Add(SettingsGroupTitle("Actor Relevancy"));
        AddFloatEditor(panel, "Spatial Cell Size", runtime.SpatialCellSize, 0.01f,
            value => UpdateProject("Change spatial cell size", project => project.Runtime.SpatialCellSize = value));

        panel.Children.Add(SettingsGroupTitle("Partition Interest"));
        AddEnumEditor(panel, "Partition Resolver", runtime.PartitionResolver,
            value => UpdateProject("Change partition resolver", project =>
            {
                project.Runtime.PartitionResolver = value;
                if (value == OdPartitionResolverKind.Custom)
                {
                    OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
                        project, OdGameplayMachineFunction.OnQueryPartitions);
                    OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
                        project, OdGameplayMachineFunction.TryResolvePartition);
                }
            }));
        if (runtime.PartitionResolver != OdPartitionResolverKind.None)
        {
            AddFloatEditor(panel, "Partition Cell Size", runtime.PartitionCellSize, 0.01f,
                value => UpdateProject("Change partition cell size", project => project.Runtime.PartitionCellSize = value));
            if (runtime.PartitionResolver == OdPartitionResolverKind.Grid)
                AddCheckEditor(panel, "Include Y Axis", runtime.PartitionIncludeY,
                    value => UpdateProject("Change partition grid axes", project => project.Runtime.PartitionIncludeY = value));

            panel.Children.Add(SettingsGroupTitle("Partition Loading"));
            AddCheckEditor(panel, "Manage Partition Loading", runtime.ManagePartitionLoading,
                value => UpdateProject("Change automatic partition loading", project => project.Runtime.ManagePartitionLoading = value));
            AddCheckEditor(panel, "Provision Missing Partitions", runtime.ProvisionPartitions,
                value => UpdateProject("Change dynamic partition provisioning", project =>
                {
                    project.Runtime.ProvisionPartitions = value;
                    if (value)
                    {
                        OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
                            project, OdGameplayMachineFunction.OnProvisionPartitions);
                    }
                }));
            AddFloatEditor(panel, "Load Margin", runtime.LoadMargin, 0f,
                value => UpdateProject("Change partition load margin", project => project.Runtime.LoadMargin = value));
            AddFloatEditor(panel, "Unload Delay (seconds)", runtime.UnloadDelaySeconds, 0f,
                value => UpdateProject("Change partition unload delay", project => project.Runtime.UnloadDelaySeconds = value));
            AddIntEditor(panel, "Max Loads Per Update", runtime.MaxLoadsPerUpdate, 1,
                value => UpdateProject("Change partition load budget", project => project.Runtime.MaxLoadsPerUpdate = value));
        }
        return panel;
    }

    private void AddNetworkModeEditor(StackPanel panel, OdNetworkMode value)
    {
        panel.Children.Add(SectionTitle("Synchronization"));
        var editor = new ComboBox
        {
            ItemsSource = Enum.GetValues<OdNetworkMode>(),
            SelectedItem = value,
        };
        bool restoring = false;
        editor.SelectionChanged += async (_, _) =>
        {
            if (restoring || editor.SelectedItem is not OdNetworkMode selected || selected == value)
                return;
            bool confirmed = await ShowConfirmAsync(
                "Change network type",
                "Changing the underlying network type can discard or rewrite incompatible project data. Back up the project before continuing.",
                "Change Type");
            if (!confirmed)
            {
                restoring = true;
                editor.SelectedItem = value;
                restoring = false;
                return;
            }

            UpdateProject("Change synchronization mode", project =>
            {
                project.Runtime.NetworkMode = selected;
                if (selected == OdNetworkMode.Lockstep)
                {
                    foreach (OdInterfaceDefinition input in project.Definitions
                                 .OfType<OdInterfaceDefinition>()
                                 .Where(item => item.InterfaceType == OdInterfaceType.PlayerInput))
                        input.Prediction = OdPredictionMode.None;
                }
            });
            RefreshRunnerConfigurationState();
        };
        panel.Children.Add(editor);
    }

    private Control BuildOdOverview(OdScope scope)
    {
        var project = _browsedProject ?? _session!.Project;
        int schemaCount = project.Definitions.Count(item => item.Namespace == scope.Namespace);
        int dataSetCount = project.DataSets.Count(item => project.FindOd(item)?.Id == scope.Id);
        return new StackPanel
        {
            Margin = new Thickness(28),
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                new TextBlock { Text = scope.Name, FontSize = 28, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = scope.Namespace, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 24) },
                StatBlock("Schemas", schemaCount),
                StatBlock("Static data sets", dataSetCount),
            },
        };
    }

    private Control BuildDefinitionEditor(OdDefinition definition)
    {
        _fieldRows.Clear();
        var panel = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 6,
            IsEnabled = _browsedProject is null,
        };
        string kindName = definition switch
        {
            OdSwitchStructDefinition => "Switch Struct",
            OdInterfaceDefinition { InterfaceType: OdInterfaceType.PlayerInput } => "Player Input",
            _ => definition.Kind.ToString(),
        };
        panel.Children.Add(new TextBlock { Text = $"{kindName}  {definition.Name}", FontSize = 18, FontWeight = FontWeight.SemiBold });
        if (definition is OdInterfaceDefinition { Abstract: false })
        {
            var editCode = new Button
            {
                Content = "Edit Code",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 8),
            };
            editCode.Click += async (_, _) => await EditEntityCodeAsync(definition.Id);
            panel.Children.Add(editCode);
        }
        switch (definition)
        {
            case OdClassDefinition classDefinition:
                AddFieldSection(panel, classDefinition.Id, "Fields", classDefinition.Fields);
                if (_browsedProject is null)
                    AddInheritedFieldSections(panel, classDefinition);
                break;
            case OdSwitchStructDefinition switchStructDefinition:
                AddFieldSection(panel, switchStructDefinition.Id, "Subtypes", switchStructDefinition.Fields,
                    addLabel: "Add Subtype", emptyLabel: "No subtypes");
                break;
            case OdStructDefinition { IsStructOf: not null } isStruct:
                panel.Children.Add(new TextBlock
                {
                    Text = $"Strict value type wrapping {TypeDisplayName(isStruct.IsStructOf.BuiltIn, isStruct.IsStructOf.DefinitionId)}",
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 12),
                });
                break;
            case OdFieldContainerDefinition container:
                AddFieldSection(panel, container.Id, "Fields", container.Fields);
                break;
            case OdInterfaceDefinition interfaceDefinition:
                AddFieldSection(panel, interfaceDefinition.Id, "Inputs", interfaceDefinition.Inputs, false);
                bool isPlayerInput = interfaceDefinition.InterfaceType == OdInterfaceType.PlayerInput;
                string outputTitle = isPlayerInput ? "Outputs  (not available for PlayerInput)" : "Outputs";
                AddFieldSection(panel, interfaceDefinition.Id, outputTitle, interfaceDefinition.Outputs, true,
                    !isPlayerInput);
                break;
            case OdEnumDefinition enumDefinition:
                AddEnumSection(panel, enumDefinition);
                break;
        }
        return new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    private Control BuildReadOnlyPackageInspector(OdProject project)
    {
        var panel = InspectorPanel("Referenced Package");
        AddReadOnly(panel, "Name", project.Name);
        AddReadOnly(panel, "Package ID", project.Id.ToString("D"));
        AddReadOnly(panel, "Description", project.Description);
        panel.Children.Add(new TextBlock
        {
            Text = "This package is being browsed read-only.",
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        return panel;
    }

    private Control BuildReadOnlyEntityInspector(OdEntity entity)
    {
        var panel = InspectorPanel(EntityKindLabel(entity) + " · Read Only");
        AddReadOnly(panel, "Name", entity.Name);
        AddReadOnly(panel, "Permanent ID", entity.Id.ToString("D"));
        if (entity is OdDefinition definition)
            AddReadOnly(panel, "Namespace", definition.Namespace);
        if (entity is OdClassDefinition classDefinition)
            AddReadOnly(panel, "Prevent Inheritance", classDefinition.PreventInheritance ? "Yes" : "No");
        if (!string.IsNullOrWhiteSpace(entity.Description))
            AddReadOnly(panel, "Description", entity.Description);
        return panel;
    }

    private void AddInheritedFieldSections(StackPanel panel, OdClassDefinition ownerClass)
    {
        IReadOnlyList<OdClassDefinition> inheritedClasses =
            _session!.Project.GetInheritedClasses(ownerClass, ReferencedDefinitions());
        if (inheritedClasses.Count == 0)
            return;

        panel.Children.Add(new TextBlock
        {
            Text = "Inherited Fields",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 14, 0, 2),
        });

        foreach (OdClassDefinition sourceClass in inheritedClasses)
            panel.Children.Add(BuildInheritedFieldSection(ownerClass, sourceClass));
    }

    private Control BuildInheritedFieldSection(OdClassDefinition ownerClass, OdClassDefinition sourceClass)
    {
        string stateKey = InheritedFieldSectionKey(ownerClass.Id, sourceClass.Id);
        bool collapsed = _session!.Project.EditorState.CollapsedInheritedFieldSections.Contains(stateKey);
        var fieldsPanel = new StackPanel { Spacing = 2, IsVisible = !collapsed };
        foreach (OdFieldDefinition field in sourceClass.Fields)
            fieldsPanel.Children.Add(BuildInheritedFieldRow(ownerClass, sourceClass, field));
        if (sourceClass.Fields.Count == 0)
            fieldsPanel.Children.Add(new TextBlock { Text = "No fields", Foreground = Brushes.Gray, Margin = new Thickness(9, 7) });

        var marker = new TextBlock
        {
            Text = collapsed ? ">" : "v",
            Width = 18,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var header = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 5),
            Margin = new Thickness(0, 2),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    marker,
                    new TextBlock { Text = sourceClass.Name, FontWeight = FontWeight.SemiBold },
                },
            },
        };
        header.Click += async (_, _) =>
        {
            collapsed = !collapsed;
            marker.Text = collapsed ? ">" : "v";
            fieldsPanel.IsVisible = !collapsed;
            if (collapsed)
                _session.Project.EditorState.CollapsedInheritedFieldSections.Add(stateKey);
            else
                _session.Project.EditorState.CollapsedInheritedFieldSections.Remove(stateKey);
            await PersistEditorStateAsync();
        };

        return new StackPanel { Children = { header, fieldsPanel } };
    }

    private Control BuildInheritedFieldRow(OdClassDefinition ownerClass, OdClassDefinition sourceClass,
        OdFieldDefinition field)
    {
        bool isDictionary = field.Type.Container == OdContainerKind.Dictionary;
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(isDictionary ? "Auto,Auto,Auto,Auto,*" : "Auto,Auto,Auto,*"),
        };
        row.Children.Add(ReadOnlyFieldCell(field.Name, 160));

        int typeColumn = 1;
        if (isDictionary)
        {
            var keyType = ReadOnlyFieldCell("Key: " + TypeDisplayName(field.Type.DictionaryKeyBuiltIn,
                field.Type.DictionaryKeyDefinitionId, field.Type.DictionaryKeyIsResourceClass), 150);
            Grid.SetColumn(keyType, typeColumn++);
            row.Children.Add(keyType);
        }

        string typePrefix = isDictionary ? "Value: " : "Type: ";
        var valueType = ReadOnlyFieldCell(typePrefix + TypeDisplayName(field.Type.BuiltIn, field.Type.DefinitionId,
                field.Type.IsResourceClass),
            isDictionary ? 150 : 180);
        Grid.SetColumn(valueType, typeColumn++);
        row.Children.Add(valueType);

        var container = ReadOnlyFieldCell(ContainerDisplayName(field.Type.Container), 120);
        Grid.SetColumn(container, typeColumn);
        row.Children.Add(container);

        bool selected = _inheritedFieldSelection?.FieldId == field.Id;
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse(selected ? "#094771" : "#252526")),
            BorderBrush = new SolidColorBrush(Color.Parse(selected ? "#007ACC" : "#3C3C3C")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5),
            Margin = new Thickness(0, 2),
            Child = row,
        };
        _fieldRows[field.Id] = border;
        border.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
        {
            SelectInheritedField(ownerClass, sourceClass, field);
            RefreshFieldRowSelection();
        }, RoutingStrategies.Tunnel);
        return border;
    }

    private static Border ReadOnlyFieldCell(string text, double width) =>
        new()
        {
            Width = width,
            MinHeight = 32,
            Margin = new Thickness(0, 0, 5, 0),
            Padding = new Thickness(9, 5),
            Background = new SolidColorBrush(Color.Parse("#2D2D30")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3C3C3C")),
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
        };

    private static string InheritedFieldSectionKey(Guid ownerClassId, Guid sourceClassId) =>
        $"{ownerClassId:N}:{sourceClassId:N}";

    private void AddFieldSection(StackPanel panel, Guid definitionId, string title,
        IReadOnlyCollection<OdFieldDefinition> fields, bool outputs = false, bool canAdd = true,
        string addLabel = "Add Field", string emptyLabel = "No fields")
    {
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 10, 0, 4) };
        heading.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var add = new Button { Content = addLabel, HorizontalAlignment = HorizontalAlignment.Right, IsEnabled = canAdd };
        Grid.SetColumn(add, 1);
        heading.Children.Add(add);
        add.Click += (_, _) => AddField(definitionId, outputs);
        panel.Children.Add(heading);

        foreach (var field in fields)
            panel.Children.Add(BuildFieldRow(field));
        if (fields.Count == 0)
            panel.Children.Add(new TextBlock { Text = emptyLabel, Foreground = Brushes.Gray, Margin = new Thickness(4, 10) });
    }

    private Control BuildFieldRow(OdFieldDefinition field)
    {
        bool isDictionary = field.Type.Container == OdContainerKind.Dictionary;
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(isDictionary ? "Auto,Auto,Auto,Auto,*,Auto" : "Auto,Auto,Auto,*,Auto"),
        };
        var name = new TextBox { Text = field.Name, Width = 160, Margin = new Thickness(0, 0, 5, 0) };
        CommitText(name, field.Name, value => RenameEntity(field.Id, value));
        row.Children.Add(name);

        int typeColumn = 1;
        if (isDictionary)
        {
            var keyType = BuildFieldTypeButton(field, true);
            Grid.SetColumn(keyType, typeColumn++);
            row.Children.Add(keyType);
        }

        var valueType = BuildFieldTypeButton(field, false);
        Grid.SetColumn(valueType, typeColumn++);
        row.Children.Add(valueType);

        var container = ContainerCombo(field.Type.Container);
        container.Width = 120;
        container.SelectionChanged += (_, _) =>
        {
            if (container.SelectedItem is OdContainerKind value)
                UpdateEntity(field.Id, $"Change container of {field.Name}", entity =>
                {
                    var target = (OdFieldDefinition)entity;
                    target.Type.Container = value;
                    if (value != OdContainerKind.Single)
                        target.NotNull = false;
                });
        };
        Grid.SetColumn(container, typeColumn++);
        row.Children.Add(container);

        var flags = new StackPanel { Orientation = Orientation.Horizontal };
        if (!field.Type.IsResourceClass && IsInterfaceInputField(field.Id) && IsSingleClassType(field.Type))
        {
            var notNull = new CheckBox
            {
                Content = "NotNull",
                IsChecked = field.NotNull,
                Margin = new Thickness(5, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            notNull.IsCheckedChanged += (_, _) =>
                UpdateEntity(field.Id, $"Change NotNull contract of {field.Name}",
                    entity => ((OdFieldDefinition)entity).NotNull = notNull.IsChecked == true);
            flags.Children.Add(notNull);
        }
        Grid.SetColumn(flags, typeColumn);
        row.Children.Add(flags);

        var remove = new Button
        {
            Content = new PathIcon
            {
                Data = StreamGeometry.Parse("M6 7 L18 7 L17 21 L7 21 Z M9 3 L15 3 L16 5 L20 5 L20 7 L4 7 L4 5 L8 5 Z M9 9 L11 9 L11 18 L9 18 Z M13 9 L15 9 L15 18 L13 18 Z"),
                Width = 15,
                Height = 15,
            },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(5, 0, 0, 0),
        };
        ToolTip.SetTip(remove, IsSwitchStructField(field.Id) ? "Delete subtype" : "Delete field");
        remove.Click += (_, _) => DeleteEntity(field);
        Grid.SetColumn(remove, typeColumn + 1);
        row.Children.Add(remove);

        bool selected = _selectedEntity?.Id == field.Id;
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse(selected ? "#094771" : "#252526")),
            BorderBrush = new SolidColorBrush(Color.Parse(selected ? "#007ACC" : "#3C3C3C")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5),
            Margin = new Thickness(0, 2),
            Child = row,
        };
        _fieldRows[field.Id] = border;
        border.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
        {
            SelectEmbeddedEntity(_session!.Project.FindEntity(field.Id));
            RefreshFieldRowSelection();
        }, RoutingStrategies.Tunnel);
        return border;
    }

    private Button BuildFieldTypeButton(OdFieldDefinition field, bool dictionaryKey)
    {
        OdBuiltInType builtIn = dictionaryKey ? field.Type.DictionaryKeyBuiltIn : field.Type.BuiltIn;
        Guid? definitionId = dictionaryKey ? field.Type.DictionaryKeyDefinitionId : field.Type.DefinitionId;
        bool isResourceClass = dictionaryKey ? field.Type.DictionaryKeyIsResourceClass : field.Type.IsResourceClass;
        string prefix = dictionaryKey ? "Key: " : field.Type.Container == OdContainerKind.Dictionary ? "Value: " : "Type: ";
        var button = new Button
        {
            Content = prefix + TypeDisplayName(builtIn, definitionId, isResourceClass),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Width = field.Type.Container == OdContainerKind.Dictionary ? 150 : 180,
            Margin = new Thickness(0, 0, 5, 0),
        };
        button.Click += async (_, _) =>
        {
            bool resourceField = FindOwningDefinition(field) is OdResourceDefinition;
            IEnumerable<TypeChoice> available = dictionaryKey ? DictionaryKeyTypeChoices() : TypeChoices(resourceField);
            var choices = (resourceField ? ResourceFieldTypeChoices(available) : available).ToList();
            TypeChoice? current = FindTypeChoice(choices, builtIn, definitionId, isResourceClass);
            TypeChoice? choice = await PickTypeAsync(dictionaryKey ? "Select Map Key Type" : "Select Field Type", choices, current);
            if (choice is null)
                return;
            bool choiceIsClass = choice.DefinitionId is { } choiceId &&
                                 AllDefinitions().Any(item => item.Id == choiceId && item is OdClassDefinition);
            bool choiceIsInterface = choice.DefinitionId is { } interfaceId &&
                                     AllDefinitions().Any(item => item.Id == interfaceId && item is OdInterfaceDefinition);
            UpdateEntity(field.Id, dictionaryKey ? $"Change map key type of {field.Name}" : $"Change type of {field.Name}", entity =>
            {
                var target = (OdFieldDefinition)entity;
                if (dictionaryKey)
                {
                    target.Type.DictionaryKeyBuiltIn = choice.BuiltIn;
                    target.Type.DictionaryKeyDefinitionId = choice.DefinitionId;
                    target.Type.DictionaryKeyIsResourceClass = choice.IsResourceClass;
                }
                else
                {
                    target.Type.BuiltIn = choice.BuiltIn;
                    target.Type.DefinitionId = choice.DefinitionId;
                    target.Type.IsResourceClass = choice.IsResourceClass;
                    if (choiceIsInterface)
                        target.Type.Container = OdContainerKind.Single;
                    if (!choiceIsClass || choice.IsResourceClass || target.Type.Container != OdContainerKind.Single)
                        target.NotNull = false;
                }
            });
        };
        return button;
    }

    private void RefreshFieldRowSelection()
    {
        foreach ((Guid id, Border border) in _fieldRows)
        {
            bool selected = _selectedEntity?.Id == id;
            border.Background = new SolidColorBrush(Color.Parse(selected ? "#094771" : "#252526"));
            border.BorderBrush = new SolidColorBrush(Color.Parse(selected ? "#007ACC" : "#3C3C3C"));
        }
    }

    private void AddEnumSection(StackPanel panel, OdEnumDefinition definition)
    {
        var add = new Button { Content = "Add Member", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 4) };
        add.Click += (_, _) =>
        {
            var member = new OdEnumMember { Name = NextName(definition.Members.Select(item => item.Name), "Value"), Value = definition.Members.Count == 0 ? 0 : definition.Members.Max(item => item.Value) + 1 };
            _session!.Execute(OdChangeKind.Create, member.Id, nameof(OdEnumMember), $"Add enum member {member.Name}",
                project => ((OdEnumDefinition)project.Definitions.Single(item => item.Id == definition.Id)).Members.Add(member));
            _selectedEntity = member;
        };
        panel.Children.Add(add);
        foreach (var member in definition.Members)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2) };
            var name = new TextBox { Text = member.Name, Margin = new Thickness(0, 0, 5, 0) };
            CommitText(name, member.Name, value => RenameEntity(member.Id, value));
            row.Children.Add(name);
            var remove = new Button { Content = "Remove", Margin = new Thickness(5, 0, 0, 0) };
            remove.Click += (_, _) => DeleteEntity(member);
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            row.PointerPressed += (_, _) => SelectEmbeddedEntity(FindEntityAcrossPackages(member.Id));
            panel.Children.Add(row);
        }
    }

    private Control BuildDataSetEditor(OdDataSet dataSet)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 5,
            IsEnabled = _browsedProject is null,
        };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(new TextBlock { Text = $"Static Data: {dataSet.Name}", FontSize = 18, FontWeight = FontWeight.SemiBold });
        var add = new Button { Content = "Add Record" };
        Grid.SetColumn(add, 1);
        heading.Children.Add(add);
        add.Click += (_, _) =>
        {
            var record = new OdDataRecord { Name = NextName(dataSet.Records.Select(item => item.Name), "Record") };
            _session!.Execute(OdChangeKind.Create, record.Id, nameof(OdDataRecord), $"Add data record {record.Name}",
                project => project.DataSets.Single(item => item.Id == dataSet.Id).Records.Add(record));
            _selectedEntity = record;
        };
        panel.Children.Add(heading);
        var displayedProject = _browsedProject ?? _session!.Project;
        var definition = displayedProject.Definitions.OfType<OdFieldContainerDefinition>()
            .FirstOrDefault(item => item.Id == dataSet.DefinitionId);
        foreach (var record in dataSet.Records)
            panel.Children.Add(BuildDataRecord(record, definition));
        return new ScrollViewer { Content = panel };
    }

    private Control BuildDataRecord(OdDataRecord record, OdFieldContainerDefinition? definition)
    {
        var body = new StackPanel { Spacing = 3 };
        var name = new TextBox { Text = record.Name, FontWeight = FontWeight.SemiBold };
        CommitText(name, record.Name, value => RenameEntity(record.Id, value));
        body.Children.Add(name);
        if (definition is not null)
        {
            foreach (var field in definition.Fields)
            {
                var value = record.Values.TryGetValue(field.Id, out var node) ? node?.ToJsonString() ?? "null" : "null";
                var editor = new TextBox { Text = value };
                CommitText(editor, value, text =>
                {
                    JsonNode? parsed;
                    try { parsed = JsonNode.Parse(text); }
                    catch { SetStatus($"Invalid JSON value for {field.Name}"); return; }
                    UpdateEntity(record.Id, $"Change {record.Name}.{field.Name}", entity => ((OdDataRecord)entity).Values[field.Id] = parsed);
                });
                body.Children.Add(new TextBlock { Text = field.Name, Foreground = Brushes.Gray, FontSize = 11 });
                body.Children.Add(editor);
            }
        }
        var border = new Border { BorderBrush = new SolidColorBrush(Color.Parse("#3C3C3C")), BorderThickness = new Thickness(1), Padding = new Thickness(9), Margin = new Thickness(0, 3), Child = body };
        border.PointerPressed += (_, _) => SelectEmbeddedEntity(FindEntityAcrossPackages(record.Id));
        return border;
    }

    private Control BuildEntityInspector(OdEntity entity)
    {
        var panel = InspectorPanel(EntityKindLabel(entity));
        AddTextEditor(panel, "Name", entity.Name, value => RenameEntity(entity.Id, value));
        AddTextEditor(panel, "Description", entity.Description, value => UpdateEntity(entity.Id, $"Update {entity.Name} description", item => item.Description = value), true);
        if (entity is OdScope scope)
            AddTextEditor(panel, "Namespace", scope.Namespace, value => UpdateOdNamespace(scope.Id, value));
        if (entity is OdClassDefinition classDefinition)
            AddClassInspector(panel, classDefinition);
        if (entity is OdStructDefinition structDefinition)
        {
            AddCheckEditor(panel, "IsStruct (strict type alias)", structDefinition.IsStructOf is not null,
                value => UpdateEntity(entity.Id, $"Change IsStruct mode of {entity.Name}", item =>
                {
                    var target = (OdStructDefinition)item;
                    target.IsStructOf = value ? OdTypeReference.BuiltInType(OdBuiltInType.Int32) : null;
                    if (value)
                        target.Fields.Clear();
                }));
            if (structDefinition.IsStructOf is { } aliasType)
            {
                panel.Children.Add(SectionTitle("Underlying Type"));
                var aliasTypeButton = new Button
                {
                    Content = TypeDisplayName(aliasType.BuiltIn, aliasType.DefinitionId),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                aliasTypeButton.Click += async (_, _) =>
                {
                    List<TypeChoice> choices = IsStructTypeChoices(structDefinition.Id).ToList();
                    TypeChoice? current = FindTypeChoice(choices, aliasType.BuiltIn, aliasType.DefinitionId);
                    TypeChoice? selected = await PickTypeAsync("Select IsStruct Underlying Type", choices, current);
                    if (selected is null)
                        return;
                    UpdateEntity(entity.Id, $"Change underlying type of {entity.Name}", item =>
                        ((OdStructDefinition)item).IsStructOf = new OdTypeReference
                        {
                            BuiltIn = selected.BuiltIn,
                            DefinitionId = selected.DefinitionId,
                        });
                };
                panel.Children.Add(aliasTypeButton);
            }
        }
        if (entity is OdInterfaceDefinition interfaceDefinition)
        {
            if (!interfaceDefinition.Abstract)
            {
                var editCode = new Button { Content = "Edit Code", HorizontalAlignment = HorizontalAlignment.Stretch };
                editCode.Click += async (_, _) => await EditEntityCodeAsync(interfaceDefinition.Id);
                panel.Children.Add(editCode);
            }
            AddCheckEditor(panel, "Abstract Contract", interfaceDefinition.Abstract, value =>
                UpdateEntity(entity.Id, $"Change abstract mode of {entity.Name}", item =>
                    ((OdInterfaceDefinition)item).Abstract = value));
            panel.Children.Add(SectionTitle("Inheritance"));
            var baseButton = new Button
            {
                Content = interfaceDefinition.BaseInterfaceId is { } currentBaseId
                    ? AllDefinitions().OfType<OdInterfaceDefinition>().FirstOrDefault(item => item.Id == currentBaseId)?.Name ?? "Missing interface"
                    : "None",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            baseButton.Click += async (_, _) =>
            {
                var choices = new List<TypeChoice>
                {
                    new(OdBuiltInType.None, null, "None", "Interface", "Interface"),
                };
                choices.AddRange(AllDefinitions().OfType<OdInterfaceDefinition>()
                    .Where(item => item.Id != interfaceDefinition.Id &&
                                   item.InterfaceType == interfaceDefinition.InterfaceType &&
                                   item.IsRoutine == interfaceDefinition.IsRoutine)
                    .Select(item => DefinitionTypeChoice(item)));
                TypeChoice? current = FindTypeChoice(choices, OdBuiltInType.None, interfaceDefinition.BaseInterfaceId);
                TypeChoice? selected = await PickTypeAsync("Select Base Interface", choices, current);
                if (selected is null)
                    return;
                UpdateEntity(entity.Id, $"Change base interface of {entity.Name}", item =>
                    ((OdInterfaceDefinition)item).BaseInterfaceId = selected.DefinitionId);
            };
            panel.Children.Add(baseButton);
            if (interfaceDefinition.InterfaceType == OdInterfaceType.Gameplay &&
                _session!.Project.Runtime.NetworkMode != OdNetworkMode.Lockstep)
                AddCheckEditor(panel, "Routine", interfaceDefinition.IsRoutine, value => UpdateEntity(entity.Id, $"Change routine mode of {entity.Name}", item => ((OdInterfaceDefinition)item).IsRoutine = value));
            if (interfaceDefinition.InterfaceType == OdInterfaceType.PlayerInput &&
                _session!.Project.Runtime.NetworkMode == OdNetworkMode.StateSync)
                AddEnumEditor(panel, "Prediction Type", interfaceDefinition.Prediction, value =>
                    UpdateEntity(entity.Id, $"Change prediction type of {entity.Name}", item =>
                        ((OdInterfaceDefinition)item).Prediction = value));
        }
        if (entity is OdEventDefinition eventDefinition)
            AddCheckEditor(panel, "Multicast", eventDefinition.Multicast, value =>
                UpdateEntity(entity.Id, $"Change multicast mode of {entity.Name}", item =>
                    ((OdEventDefinition)item).Multicast = value));
        if (entity is OdFieldDefinition field)
        {
            if (IsClassField(field.Id))
            {
                AddEnumEditor(panel, "Field Mode", field.Mode,
                    value => UpdateEntity(entity.Id, $"Change mode of {entity.Name}", item =>
                    {
                        var target = (OdFieldDefinition)item;
                        target.Mode = value;
                        if (value == OdFieldMode.Driven)
                            target.Replication = OdFieldReplicationMode.None;
                    }));
                if (field.Mode == OdFieldMode.Driven && FindClassFieldOwner(field.Id) is { } owner)
                {
                    var editCode = new Button { Content = "Edit Driven Code", HorizontalAlignment = HorizontalAlignment.Stretch };
                    editCode.Click += async (_, _) => await EditEntityCodeAsync(field.Id);
                    panel.Children.Add(editCode);
                    AddDrivenByInspector(panel, owner, field);
                }
                panel.Children.Add(SectionTitle("Networking"));
                var displayedReplication = field.Mode == OdFieldMode.Driven
                    ? OdFieldReplicationMode.None
                    : field.Replication;
                var replication = EnumCombo(displayedReplication);
                replication.IsEnabled = field.Mode != OdFieldMode.Driven;
                replication.SelectionChanged += (_, _) =>
                {
                    if (field.Mode != OdFieldMode.Driven && replication.SelectedItem is OdFieldReplicationMode value)
                        UpdateEntity(entity.Id, $"Change replication of {entity.Name}", item => ((OdFieldDefinition)item).Replication = value);
                };
                panel.Children.Add(replication);
            }
            if (FindOwningDefinition(field) is OdFieldContainerDefinition)
            {
                panel.Children.Add(SectionTitle("Default Value"));
                panel.Children.Add(ConfiguredValueEditor.Build(ConfigurationProject(), field.Type,
                    field.DefaultValue?.DeepClone(), null,
                    value => UpdateEntity(field.Id, $"Change default value of {field.Name}", item =>
                        ((OdFieldDefinition)item).DefaultValue = value?.DeepClone())));
                var resetDefault = new Button
                {
                    Content = "Reset to type default",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                resetDefault.Click += (_, _) => UpdateEntity(field.Id, $"Reset default value of {field.Name}", item =>
                    ((OdFieldDefinition)item).DefaultValue = null);
                panel.Children.Add(resetDefault);
            }
        }
        if (entity is OdDataSet dataSet)
            AddDataSetInspector(panel, dataSet);
        AddDeleteInspector(panel, entity);
        return panel;
    }

    private async Task EditEntityCodeAsync(Guid ownerId)
    {
        if (_session is null || _projectDirectory is null)
            return;
        if (await ResolveExternalProjectChangesIfNeededAsync())
            return;
        try
        {
            OdProjectStore.RefreshCodeFilesFromDisk(_projectDirectory, _session.Project);
            OdCodeFile file = OdProjectCodeManager.GetOrCreateForEntity(_session.Project, ownerId);
            await SaveCodeFilesAndRefreshSnapshotAsync();
            OdCodeWorkspace workspace = await OdCodeWorkspaceService.PrepareAsync(_session.Project);
            CodeEditorLauncher.Open(workspace.LaunchPath, Path.Combine(_projectDirectory, "code", file.RelativePath));
            SetStatus($"Editing {file.RelativePath}");
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("Cannot edit code", exception.Message);
        }
    }

    private async Task PrepareProjectCodeAsync()
    {
        if (_session is null || _projectDirectory is null)
            return;
        OdProjectStore.RefreshCodeFilesFromDisk(_projectDirectory, _session.Project);
        OdProjectCodeManager.EnsureCodeFiles(_session.Project);
        await SaveCodeFilesAndRefreshSnapshotAsync();
    }

    private async Task EditGameplayMachineFunctionAsync(OdGameplayMachineFunction function)
    {
        if (_session is null || _projectDirectory is null)
            return;
        if (await ResolveExternalProjectChangesIfNeededAsync())
            return;
        try
        {
            OdProjectStore.RefreshCodeFilesFromDisk(_projectDirectory, _session.Project);
            OdCodeFile file = OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
                _session.Project, function);
            await SaveCodeFilesAndRefreshSnapshotAsync();
            OdCodeWorkspace workspace = await OdCodeWorkspaceService.PrepareAsync(_session.Project);
            CodeEditorLauncher.Open(
                workspace.LaunchPath,
                Path.Combine(_projectDirectory, "code", file.RelativePath));
            RefreshGameplayMachineFunctions();
            SetStatus($"Editing {file.RelativePath}");
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("Cannot edit GameplayMachine function", exception.Message);
        }
    }

    private Control BuildInheritedFieldInspector(OdFieldDefinition field, OdClassDefinition sourceClass)
    {
        var panel = InspectorPanel("Inherited Field");
        AddReadOnly(panel, "Name", field.Name);
        AddReadOnly(panel, "Declared In", sourceClass.Name);
        if (field.Type.Container == OdContainerKind.Dictionary)
            AddReadOnly(panel, "Key Type", TypeDisplayName(field.Type.DictionaryKeyBuiltIn,
                field.Type.DictionaryKeyDefinitionId, field.Type.DictionaryKeyIsResourceClass));
        AddReadOnly(panel, "Type", TypeDisplayName(field.Type.BuiltIn, field.Type.DefinitionId,
            field.Type.IsResourceClass));
        AddReadOnly(panel, "Collection", ContainerDisplayName(field.Type.Container));
        AddReadOnly(panel, "Field Mode", field.Mode.ToString());
        panel.Children.Add(new TextBlock
        {
            Text = "跳转到该类去编辑",
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 12, 0, 3),
            TextWrapping = TextWrapping.Wrap,
        });
        var navigate = new Button { Content = "跳转到该类去编辑", HorizontalAlignment = HorizontalAlignment.Stretch };
        navigate.Click += (_, _) => SelectEntity(sourceClass);
        panel.Children.Add(navigate);
        return panel;
    }

    private bool IsClassField(Guid fieldId) =>
        _session!.Project.Definitions.OfType<OdClassDefinition>()
            .Any(definition => definition.Fields.Any(field => field.Id == fieldId));

    private bool IsInterfaceInputField(Guid fieldId) =>
        _session!.Project.Definitions.OfType<OdInterfaceDefinition>()
            .Any(definition => definition.Inputs.Any(field => field.Id == fieldId));

    private bool IsSingleClassType(OdTypeReference type) =>
        type.Container == OdContainerKind.Single &&
        IsClassType(type);

    private bool IsClassType(OdTypeReference type) =>
        type.DefinitionId is { } id &&
        AllDefinitions().Any(definition => definition.Id == id && definition is OdClassDefinition);

    private OdClassDefinition? FindClassFieldOwner(Guid fieldId) =>
        _session!.Project.Definitions.OfType<OdClassDefinition>()
            .FirstOrDefault(definition => definition.Fields.Any(field => field.Id == fieldId));

    private void AddDrivenByInspector(StackPanel panel, OdClassDefinition owner, OdFieldDefinition field)
    {
        panel.Children.Add(SectionTitle("Driven By"));
        List<DrivenFieldChoice> available = new();
        available.AddRange(owner.Fields.Where(item => item.Id != field.Id)
            .Select(item => new DrivenFieldChoice(item.Id, item.Name)));
        foreach (OdClassDefinition inherited in _session!.Project.GetInheritedClasses(owner))
            available.AddRange(inherited.Fields.Select(item =>
                new DrivenFieldChoice(item.Id, $"{inherited.Name}.{item.Name}")));

        foreach (Guid drivenById in field.DrivenByFields)
        {
            DrivenFieldChoice? choice = available.FirstOrDefault(item => item.Id == drivenById);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24"), Margin = new Thickness(0, 1) };
            row.Children.Add(new TextBlock
            {
                Text = choice?.Label ?? $"Missing field ({drivenById})",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = choice is null ? Brushes.IndianRed : Brushes.White,
            });
            var remove = new Button
            {
                Content = new PathIcon
                {
                    Data = StreamGeometry.Parse("M5 11 L19 11 L19 13 L5 13 Z"),
                    Width = 12,
                    Height = 12,
                },
                Width = 22,
                Height = 22,
                MinWidth = 22,
                MinHeight = 22,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(remove, "Remove dependency");
            remove.Click += (_, _) => UpdateEntity(field.Id, $"Remove DrivenBy from {field.Name}", entity =>
                ((OdFieldDefinition)entity).DrivenByFields.RemoveAll(id => id == drivenById));
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            panel.Children.Add(row);
        }

        List<DrivenFieldChoice> choices = available
            .Where(item => !field.DrivenByFields.Contains(item.Id))
            .OrderBy(item => item.Label, StringComparer.Ordinal)
            .ToList();
        var addRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4 };
        var picker = new ComboBox
        {
            ItemsSource = choices,
            PlaceholderText = "Select field...",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        addRow.Children.Add(picker);
        var add = new Button { Content = "Add", IsEnabled = choices.Count > 0 };
        add.Click += (_, _) =>
        {
            if (picker.SelectedItem is DrivenFieldChoice selected)
                UpdateEntity(field.Id, $"Add DrivenBy to {field.Name}", entity =>
                    ((OdFieldDefinition)entity).DrivenByFields.Add(selected.Id));
        };
        Grid.SetColumn(add, 1);
        addRow.Children.Add(add);
        panel.Children.Add(addRow);
    }

    private bool IsSwitchStructField(Guid fieldId) =>
        _session!.Project.Definitions.OfType<OdSwitchStructDefinition>()
            .Any(definition => definition.Fields.Any(field => field.Id == fieldId));

    private void AddClassInspector(StackPanel panel, OdClassDefinition classDefinition)
    {
        AddCheckEditor(panel, "Root GameplayObject Class", _session!.Project.RootClassId == classDefinition.Id,
            value => UpdateProject(
                value ? $"Set {classDefinition.Name} as root gameplay object class" : "Clear root gameplay object class",
                project =>
                {
                    if (value)
                        project.RootClassId = classDefinition.Id;
                    else if (project.RootClassId == classDefinition.Id)
                        project.RootClassId = null;
                }));
        AddEnumEditor(panel, "Replication", classDefinition.Replication,
            value => UpdateEntity(classDefinition.Id, $"Change replication of {classDefinition.Name}",
                entity => ((OdClassDefinition)entity).Replication = value));
        AddEnumEditor(panel, "Relevancy", classDefinition.Relevancy,
            value => UpdateEntity(classDefinition.Id, $"Change relevancy of {classDefinition.Name}",
                entity => ((OdClassDefinition)entity).Relevancy = value));
        if (_session!.Project.Runtime.NetworkMode == OdNetworkMode.StateSync)
            AddEnumEditor(panel, "Partition", classDefinition.Partition,
                value => UpdateEntity(classDefinition.Id, $"Change partition mode of {classDefinition.Name}",
                    entity => ((OdClassDefinition)entity).Partition = value));
        AddCheckEditor(panel, "Prevent Inheritance", classDefinition.PreventInheritance,
            value => UpdateEntity(classDefinition.Id, $"Change inheritance policy of {classDefinition.Name}",
                entity => ((OdClassDefinition)entity).PreventInheritance = value));
        panel.Children.Add(SectionTitle("IsClasses"));
        foreach (Guid isClass in classDefinition.IsClasses)
        {
            OdClassDefinition? referencedClass = AllDefinitions().OfType<OdClassDefinition>()
                .FirstOrDefault(item => item.Id == isClass);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24"), Margin = new Thickness(0, 1) };
            row.Children.Add(new TextBlock
            {
                Text = referencedClass?.Name ?? "Missing class",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = referencedClass is null ? Brushes.IndianRed : Brushes.White,
            });
            var remove = new Button
            {
                Content = new PathIcon
                {
                    Data = StreamGeometry.Parse("M5 11 L19 11 L19 13 L5 13 Z"),
                    Width = 12,
                    Height = 12,
                },
                Width = 22,
                Height = 22,
                MinWidth = 22,
                MinHeight = 22,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(remove, "Remove IsClass");
            remove.Click += (_, _) => UpdateEntity(classDefinition.Id, $"Remove IsClass from {classDefinition.Name}", entity =>
                ((OdClassDefinition)entity).IsClasses.RemoveAll(id => id == isClass));
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            panel.Children.Add(row);
        }

        var addIsClass = new Button
        {
            Content = "+ Add IsClass",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        addIsClass.Click += async (_, _) =>
        {
            var choices = DefinitionTypeChoices(
                AllDefinitions().OfType<OdClassDefinition>()
                    .Where(item => item.Id != classDefinition.Id &&
                                   !item.PreventInheritance &&
                                   !classDefinition.IsClasses.Contains(item.Id)),
                includeNone: false).ToList();
            if (choices.Count == 0)
            {
                SetStatus("No other classes are available");
                return;
            }
            TypeChoice? choice = await PickTypeAsync("Add IsClass", choices, null);
            if (choice?.DefinitionId is { } isClass)
                UpdateEntity(classDefinition.Id, $"Add IsClass to {classDefinition.Name}", entity => ((OdClassDefinition)entity).IsClasses.Add(isClass));
        };
        panel.Children.Add(addIsClass);
    }

    private void AddDeleteInspector(StackPanel panel, OdEntity entity)
    {
        string kind = EntityKindLabel(entity);
        panel.Children.Add(SectionTitle("Danger Zone"));
        var delete = new Button
        {
            Content = $"Delete {kind}",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        delete.Click += async (_, _) =>
        {
            if (await ShowConfirmAsync($"Delete {kind.ToLowerInvariant()}",
                    $"Delete {kind.ToLowerInvariant()} '{entity.Name}'? You can undo this action.", "Delete"))
                DeleteEntity(entity);
        };
        panel.Children.Add(delete);
    }

    private string EntityKindLabel(OdEntity entity) => entity switch
    {
        OdScope => "OD",
        OdClassDefinition => "Class",
        OdStructDefinition => "Struct",
        OdResourceDefinition => "Resource",
        OdSwitchStructDefinition => "Switch Struct",
        OdEnumDefinition => "Enum",
        OdInterfaceDefinition { InterfaceType: OdInterfaceType.PlayerInput } => "Player Input",
        OdInterfaceDefinition => "Interface",
        OdEventDefinition => "Event",
        OdFieldDefinition field when IsSwitchStructField(field.Id) => "Switch Subtype",
        OdFieldDefinition => "Field",
        OdEnumMember => "Enum Member",
        OdDataSet => "Data Set",
        OdDataRecord => "Data Record",
        _ => "Item",
    };

    private void AddDataSetInspector(StackPanel panel, OdDataSet dataSet)
    {
        panel.Children.Add(SectionTitle("Record Type"));
        OdScope? ownerOd = _session!.Project.FindOd(dataSet);
        var definitions = DefinitionTypeChoices(
            _session.Project.Definitions.OfType<OdFieldContainerDefinition>()
                .Where(item => (item is OdClassDefinition or OdStructDefinition) &&
                               (ownerOd is null || item.Namespace == ownerOd.Namespace)),
            includeNone: false).ToList();
        TypeChoice? current = definitions.FirstOrDefault(item => item.DefinitionId == dataSet.DefinitionId);
        var picker = new Button
        {
            Content = current?.Name ?? "Select record type...",
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        picker.Click += async (_, _) =>
        {
            TypeChoice? choice = await PickTypeAsync("Select Record Type", definitions, current);
            if (choice?.DefinitionId is { } definitionId)
                UpdateEntity(dataSet.Id, $"Change type of {dataSet.Name}", entity => ((OdDataSet)entity).DefinitionId = definitionId);
        };
        panel.Children.Add(picker);
    }

    private Control BuildOwningEntityEditor(OdEntity entity)
    {
        var definition = _session!.Project.Definitions.FirstOrDefault(item => OdProject.GetDefinitionChildren(item).Any(child => child.Id == entity.Id));
        if (definition is not null)
            return BuildDefinitionEditor(definition);
        var dataSet = _session.Project.DataSets.FirstOrDefault(item => item.Records.Any(record => record.Id == entity.Id));
        return dataSet is not null ? BuildDataSetEditor(dataSet) : EmptyMessage("Select an entity from the project tree.");
    }

    private void SelectEmbeddedEntity(OdEntity? entity)
    {
        if (entity is null || _session is null)
            return;
        _inheritedFieldSelection = null;
        _selectedEntity = entity;
        _session.Project.EditorState.SelectedEntityId = entity.Id;
        InspectorHost.Content = BuildEntityInspector(entity);
        RefreshCommandState();
    }

    private void SelectInheritedField(OdClassDefinition ownerClass, OdClassDefinition sourceClass,
        OdFieldDefinition field)
    {
        _selectedEntity = field;
        _inheritedFieldSelection = new InheritedFieldSelection(ownerClass.Id, sourceClass.Id, field.Id);
        _session!.Project.EditorState.SelectedEntityId = ownerClass.Id;
        InspectorHost.Content = BuildInheritedFieldInspector(field, sourceClass);
        RefreshCommandState();
    }

    private async Task PersistEditorStateAsync()
    {
        if (_session is null || _projectDirectory is null)
            return;

        await _editorStateSaveGate.WaitAsync();
        try
        {
            await OdProjectStore.SaveEditorStateAsync(_projectDirectory, _session.Project.EditorState);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not save editor state: {exception.Message}");
        }
        finally
        {
            _editorStateSaveGate.Release();
        }
    }

    private async void OnExplorerAddClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { DataContext: ExplorerItem item } && item.AddKind is { } addKind)
            await AddExplorerItemAsync(addKind, item.OwnerOdId);
        eventArgs.Handled = true;
    }

    private async void OnExplorerEditCodeClick(object? sender, RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (sender is Button
            {
                DataContext: ExplorerItem
                {
                    Entity: OdInterfaceDefinition { Abstract: false } interfaceDefinition,
                },
            })
            await EditEntityCodeAsync(interfaceDefinition.Id);
    }

    private async Task AddExplorerItemAsync(ExplorerAddKind addKind, Guid? ownerOdId = null)
    {
        if (_session is null)
            return;
        OdScope? ownerOd = ownerOdId is { } id
            ? _session.Project.Ods.FirstOrDefault(item => item.Id == id)
            : SelectedOd();
        if (addKind != ExplorerAddKind.Od && ownerOd is null)
        {
            await ShowMessageAsync("Cannot add item", "Add an OD before creating items.");
            return;
        }
        if (addKind == ExplorerAddKind.DataSet && !_session.Project.Definitions.OfType<OdFieldContainerDefinition>()
                .Any(item => (item is OdClassDefinition or OdStructDefinition) && item.Namespace == ownerOd!.Namespace))
        {
            await ShowMessageAsync("Cannot add static data", "Add a class or struct to this OD before creating a data set.");
            return;
        }

        string? name = await PromptForItemNameAsync(addKind, ownerOd?.Id);
        if (name is null)
            return;
        switch (addKind)
        {
            case ExplorerAddKind.Od: AddOd(name); break;
            case ExplorerAddKind.Class: AddDefinition(OdDefinitionKind.Class, name, ownerOd!); break;
            case ExplorerAddKind.Struct: AddDefinition(OdDefinitionKind.Struct, name, ownerOd!); break;
            case ExplorerAddKind.Resource: AddDefinition(OdDefinitionKind.Resource, name, ownerOd!); break;
            case ExplorerAddKind.SwitchStruct: AddDefinition(OdDefinitionKind.SwitchStruct, name, ownerOd!); break;
            case ExplorerAddKind.Enum: AddDefinition(OdDefinitionKind.Enum, name, ownerOd!); break;
            case ExplorerAddKind.Interface: AddDefinition(OdDefinitionKind.Interface, name, ownerOd!); break;
            case ExplorerAddKind.PlayerInput: AddDefinition(OdDefinitionKind.Interface, name, ownerOd!, OdInterfaceType.PlayerInput); break;
            case ExplorerAddKind.Event: AddDefinition(OdDefinitionKind.Event, name, ownerOd!); break;
            case ExplorerAddKind.DataSet: AddDataSet(name, ownerOd!); break;
            default: throw new ArgumentOutOfRangeException(nameof(addKind));
        }
    }

    private void AddOd(string name)
    {
        if (_session is null)
            return;
        var od = new OdScope { Name = name, Namespace = NextOdNamespace(name) };
        _selectedEntity = od;
        _session.Project.EditorState.SelectedEntityId = od.Id;
        _session.Execute(OdChangeKind.Create, od.Id, nameof(OdScope), $"Add OD {name}", project =>
        {
            if (project.Ods.Count == 0)
                project.DefaultNamespace = od.Namespace;
            project.Ods.Add(od);
        });
    }

    private void AddDefinition(OdDefinitionKind kind, string name, OdScope ownerOd,
        OdInterfaceType interfaceType = OdInterfaceType.Gameplay)
    {
        if (_session is null)
            return;
        OdDefinition definition = kind switch
        {
            OdDefinitionKind.Class => new OdClassDefinition(),
            OdDefinitionKind.Struct => new OdStructDefinition(),
            OdDefinitionKind.Resource => new OdResourceDefinition(),
            OdDefinitionKind.SwitchStruct => new OdSwitchStructDefinition(),
            OdDefinitionKind.Enum => new OdEnumDefinition { Members = { new OdEnumMember { Name = "None", Value = 0 } } },
            OdDefinitionKind.Interface => new OdInterfaceDefinition
            {
                InterfaceType = interfaceType,
                Abstract = false,
            },
            OdDefinitionKind.Event => new OdEventDefinition(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        definition.Name = name;
        definition.Namespace = ownerOd.Namespace;
        _selectedEntity = definition;
        _session.Project.EditorState.SelectedEntityId = definition.Id;
        _session.Execute(OdChangeKind.Create, definition.Id, definition.GetType().Name, $"Add {kind} {name}", project => project.Definitions.Add(definition));
    }

    private void AddDataSet(string name, OdScope ownerOd)
    {
        if (_session is null)
            return;
        var selectedType = _selectedEntity as OdFieldContainerDefinition;
        if (selectedType?.Namespace != ownerOd.Namespace || selectedType is not (OdClassDefinition or OdStructDefinition))
            selectedType = _session.Project.Definitions.OfType<OdFieldContainerDefinition>()
                .FirstOrDefault(item => (item is OdClassDefinition or OdStructDefinition) && item.Namespace == ownerOd.Namespace);
        if (selectedType is null)
        {
            SetStatus("Add a class or struct before creating a data set");
            return;
        }
        var dataSet = new OdDataSet
        {
            Name = name,
            DefinitionId = selectedType.Id,
        };
        _selectedEntity = dataSet;
        _session.Project.EditorState.SelectedEntityId = dataSet.Id;
        _session.Execute(OdChangeKind.Create, dataSet.Id, nameof(OdDataSet), $"Add data set {dataSet.Name}", project => project.DataSets.Add(dataSet));
    }

    private void AddField(Guid definitionId, bool output)
    {
        if (_session is null)
            return;
        var definition = _session.Project.Definitions.Single(item => item.Id == definitionId);
        var fields = definition switch
        {
            OdFieldContainerDefinition container => container.Fields,
            OdInterfaceDefinition interfaceDefinition when output => interfaceDefinition.Outputs,
            OdInterfaceDefinition interfaceDefinition => interfaceDefinition.Inputs,
            _ => throw new InvalidOperationException("Definition cannot contain fields."),
        };
        bool switchSubtype = definition is OdSwitchStructDefinition;
        var field = new OdFieldDefinition
        {
            Name = NextName(fields.Select(item => item.Name), switchSubtype ? "Subtype" : "Field"),
        };
        _session.Execute(OdChangeKind.Create, field.Id, nameof(OdFieldDefinition),
            $"Add {(switchSubtype ? "subtype" : "field")} {definition.Name}.{field.Name}", project =>
        {
            var target = project.Definitions.Single(item => item.Id == definitionId);
            switch (target)
            {
                case OdFieldContainerDefinition container: container.Fields.Add(field); break;
                case OdInterfaceDefinition interfaceDefinition when output: interfaceDefinition.Outputs.Add(field); break;
                case OdInterfaceDefinition interfaceDefinition: interfaceDefinition.Inputs.Add(field); break;
            }
        });
        _selectedEntity = field;
    }

    private async Task RenameSelectedEntityAsync()
    {
        if (_session is null || _selectedEntity is null || _inheritedFieldSelection is not null)
            return;

        OdEntity entity = _selectedEntity;
        string? name = await PromptForEntityNameAsync(entity);
        if (name is not null && !string.Equals(name, entity.Name, StringComparison.Ordinal))
            RenameEntity(entity.Id, name);
    }

    private void DeleteSelectedEntity()
    {
        if (_selectedEntity is not null && _inheritedFieldSelection is null)
            DeleteEntity(_selectedEntity);
    }

    private void DeleteEntity(OdEntity entity)
    {
        if (_session is null)
            return;
        var id = entity.Id;
        OdEntity? fallback = FindDeleteFallback(entity);
        _inheritedFieldSelection = null;
        _selectedEntity = fallback;
        _session.Project.EditorState.SelectedEntityId = fallback?.Id;
        _session.Execute(OdChangeKind.Delete, id, entity.GetType().Name, $"Delete {entity.Name}", project =>
        {
            if (entity is OdScope scope)
            {
                var definitionIds = project.Definitions
                    .Where(item => item.Namespace == scope.Namespace)
                    .Select(item => item.Id)
                    .ToHashSet();
                if (project.RootClassId is { } rootClassId && definitionIds.Contains(rootClassId))
                    project.RootClassId = null;
                var sceneIds = project.Scenes.Where(item => item.OdId == scope.Id).Select(item => item.Id).ToHashSet();
                if (project.InitialSceneId is { } initialSceneId && sceneIds.Contains(initialSceneId))
                    project.InitialSceneId = null;
                project.Ods.RemoveAll(item => item.Id == id);
                project.Definitions.RemoveAll(item => definitionIds.Contains(item.Id));
                project.DataSets.RemoveAll(item => definitionIds.Contains(item.DefinitionId));
                if (project.DefaultNamespace == scope.Namespace && project.Ods.FirstOrDefault() is { } firstOd)
                    project.DefaultNamespace = firstOd.Namespace;
                return;
            }
            if (project.RootClassId == id)
                project.RootClassId = null;
            project.Definitions.RemoveAll(item => item.Id == id);
            project.DataSets.RemoveAll(item => item.Id == id);
            foreach (var definition in project.Definitions)
            {
                switch (definition)
                {
                    case OdFieldContainerDefinition container: container.Fields.RemoveAll(item => item.Id == id); break;
                    case OdInterfaceDefinition interfaceDefinition:
                        interfaceDefinition.Inputs.RemoveAll(item => item.Id == id);
                        interfaceDefinition.Outputs.RemoveAll(item => item.Id == id);
                        break;
                    case OdEnumDefinition enumDefinition: enumDefinition.Members.RemoveAll(item => item.Id == id); break;
                }
            }
            foreach (var dataSet in project.DataSets)
                dataSet.Records.RemoveAll(item => item.Id == id);
        });
    }

    private OdEntity? FindDeleteFallback(OdEntity entity)
    {
        if (_session is null || entity is OdScope)
            return null;
        if (entity is OdDefinition definition)
            return _session.Project.FindOd(definition);
        if (entity is OdDataSet dataSet)
            return _session.Project.FindOd(dataSet);
        OdDefinition? ownerDefinition = _session.Project.Definitions
            .FirstOrDefault(item => OdProject.GetDefinitionChildren(item).Any(child => child.Id == entity.Id));
        if (ownerDefinition is not null)
            return ownerDefinition;
        return _session.Project.DataSets.FirstOrDefault(item => item.Records.Any(record => record.Id == entity.Id));
    }

    private void UpdateEntity(Guid id, string summary, Action<OdEntity> mutation, OdChangeKind kind = OdChangeKind.Update)
    {
        if (_session is null)
            return;
        _session.Execute(kind, id, _session.Project.FindEntity(id)?.GetType().Name ?? "Entity", summary,
            project => mutation(project.FindEntity(id) ?? throw new InvalidOperationException($"Entity {id} no longer exists.")));
    }

    private void UpdateProject(string summary, Action<OdProject> mutation)
    {
        _session?.Execute(OdChangeKind.Update, _session.Project.Id, nameof(OdProject), summary, mutation);
    }

    private void UpdateOdNamespace(Guid odId, string value)
    {
        if (_session?.Project.FindEntity(odId) is not OdScope scope)
            return;
        string newNamespace = value.Trim();
        if (string.Equals(scope.Namespace, newNamespace, StringComparison.Ordinal))
            return;
        if (!IsValidNamespace(newNamespace))
        {
            SetStatus("Namespace must contain valid C# identifiers separated by dots");
            ShowSelectedEditor();
            return;
        }
        if (_session.Project.Ods.Any(item => item.Id != odId && item.Namespace == newNamespace))
        {
            SetStatus($"Namespace '{newNamespace}' is already used by another OD");
            ShowSelectedEditor();
            return;
        }

        string oldNamespace = scope.Namespace;
        _session.Execute(OdChangeKind.Update, odId, nameof(OdScope),
            $"Change namespace of {scope.Name} to {newNamespace}", project =>
            {
                OdScope target = project.Ods.Single(item => item.Id == odId);
                target.Namespace = newNamespace;
                foreach (OdDefinition definition in project.Definitions.Where(item => item.Namespace == oldNamespace))
                    definition.Namespace = newNamespace;
                if (project.DefaultNamespace == oldNamespace)
                    project.DefaultNamespace = newNamespace;
            });
    }

    private void RenameEntity(Guid id, string newName)
    {
        if (_session?.Project.FindEntity(id) is not { } entity || string.Equals(entity.Name, newName, StringComparison.Ordinal))
            return;
        var oldName = entity.Name;
        _session.Execute(OdChangeKind.Rename, id, entity.GetType().Name, $"Rename {oldName} to {newName}",
            project => project.FindEntity(id)!.Name = newName,
            new Dictionary<string, string?> { ["oldName"] = oldName, ["newName"] = newName });
    }

    private void RenameProject(string newName)
    {
        if (_session is null || string.Equals(_session.Project.Name, newName, StringComparison.Ordinal))
            return;
        var oldName = _session.Project.Name;
        _session.Execute(OdChangeKind.Rename, _session.Project.Id, nameof(OdProject), $"Rename {oldName} to {newName}",
            project => project.Name = newName,
            new Dictionary<string, string?> { ["oldName"] = oldName, ["newName"] = newName });
    }

    private IEnumerable<TypeChoice> TypeChoices(bool includeInterfaces = false)
    {
        foreach (var builtIn in Enum.GetValues<OdBuiltInType>().Where(item => item != OdBuiltInType.None))
            yield return new TypeChoice(builtIn, null, BuiltInTypeDisplayName(builtIn), "Built-in", "Built-in");
        if (_session is null)
            yield break;
        foreach (var definition in _session.Project.Definitions.Where(item =>
                     item is OdClassDefinition or OdStructDefinition or OdResourceDefinition or OdSwitchStructDefinition or OdEnumDefinition ||
                     includeInterfaces && item is OdInterfaceDefinition interfaceDefinition &&
                     interfaceDefinition.InterfaceType == OdInterfaceType.Gameplay &&
                     !interfaceDefinition.IsRoutine && interfaceDefinition.Outputs.Count == 0))
            yield return DefinitionTypeChoice(definition, allowResourceClass: true);
        foreach (OdProject package in _loadedReferenceProjects.Values.OrderBy(item => item.Name, StringComparer.Ordinal))
            foreach (OdDefinition definition in package.Definitions.Where(item =>
                         item is OdClassDefinition or OdStructDefinition or OdResourceDefinition or OdSwitchStructDefinition or OdEnumDefinition ||
                         includeInterfaces && item is OdInterfaceDefinition interfaceDefinition &&
                         interfaceDefinition.InterfaceType == OdInterfaceType.Gameplay &&
                         !interfaceDefinition.IsRoutine && interfaceDefinition.Outputs.Count == 0))
                yield return DefinitionTypeChoice(definition, allowResourceClass: true);
    }

    private IEnumerable<TypeChoice> DictionaryKeyTypeChoices() => TypeChoices();

    private IEnumerable<TypeChoice> ResourceFieldTypeChoices(IEnumerable<TypeChoice> choices)
    {
        foreach (TypeChoice choice in choices)
        {
            bool isClass = choice.DefinitionId is { } definitionId &&
                           AllDefinitions().Any(definition =>
                               definition.Id == definitionId && definition is OdClassDefinition);
            yield return isClass ? choice with { CanSelectDirect = false } : choice;
        }
    }

    private IEnumerable<TypeChoice> IsStructTypeChoices(Guid ownerId)
    {
        foreach (OdBuiltInType builtIn in Enum.GetValues<OdBuiltInType>()
                     .Where(item => item != OdBuiltInType.None))
            yield return new TypeChoice(builtIn, null, BuiltInTypeDisplayName(builtIn), "Built-in", "Built-in");
        foreach (OdDefinition definition in AllDefinitions().Where(item =>
                     item.Id != ownerId && item is (OdEnumDefinition or OdStructDefinition)))
            yield return DefinitionTypeChoice(definition);
    }

    private IEnumerable<TypeChoice> DefinitionTypeChoices(
        IEnumerable<OdDefinition> definitions,
        bool includeNone)
    {
        if (includeNone)
            yield return new TypeChoice(OdBuiltInType.None, null, "None", "Built-in", "Built-in");
        foreach (OdDefinition definition in definitions)
            yield return DefinitionTypeChoice(definition);
    }

    private TypeChoice DefinitionTypeChoice(OdDefinition definition, bool allowResourceClass = false,
        bool allowDirect = true)
    {
        OdProject? owner = FindOwningProject(definition.Id);
        string scope = owner?.FindOd(definition)?.Name ?? "Unassigned OD";
        string category = owner is null || owner == _session?.Project ? scope : $"{owner.Name} / {scope}";
        string detail = definition switch
        {
            OdClassDefinition => "Class",
            OdStructDefinition => "Struct",
            OdResourceDefinition => "Resource",
            OdSwitchStructDefinition => "Switch Struct",
            OdEnumDefinition => "Enum",
            OdInterfaceDefinition => "Interface",
            OdEventDefinition => "Event",
            _ => definition.Kind.ToString(),
        };
        return new TypeChoice(OdBuiltInType.None, definition.Id, definition.Name, category, detail,
            allowResourceClass && definition is OdClassDefinition &&
            OdNetworkPackage.RuntimeTypeName(definition.Id) is null, CanSelectDirect: allowDirect);
    }

    private string TypeDisplayName(OdBuiltInType builtIn, Guid? definitionId, bool isResourceClass = false)
    {
        if (definitionId is { } id)
        {
            string name = AllDefinitions().FirstOrDefault(item => item.Id == id)?.Name ?? "Missing type";
            return isResourceClass ? name + "Resource" : name;
        }
        return builtIn == OdBuiltInType.None ? "Select type..." : BuiltInTypeDisplayName(builtIn);
    }

    private static TypeChoice? FindTypeChoice(
        IEnumerable<TypeChoice> choices,
        OdBuiltInType builtIn,
        Guid? definitionId,
        bool isResourceClass = false)
    {
        TypeChoice? choice = choices.FirstOrDefault(item => item.BuiltIn == builtIn && item.DefinitionId == definitionId);
        return choice is null ? null : choice with { IsResourceClass = isResourceClass };
    }

    private async Task<TypeChoice?> PickTypeAsync(
        string title,
        IEnumerable<TypeChoice> choices,
        TypeChoice? current)
    {
        var dialog = new TypePickerDialog(title, choices, current);
        return await dialog.ShowDialog<TypeChoice?>(this);
    }

    private static string BuiltInTypeDisplayName(OdBuiltInType type) =>
        OdTypePresentation.BuiltInDisplayName(type);

    private static ComboBox EnumCombo<T>(T value) where T : struct, Enum =>
        new() { ItemsSource = Enum.GetValues<T>(), SelectedItem = value, Margin = new Thickness(0, 0, 5, 0) };

    private static ComboBox ContainerCombo(OdContainerKind value) =>
        new()
        {
            ItemsSource = Enum.GetValues<OdContainerKind>(),
            SelectedItem = value,
            Margin = new Thickness(0, 0, 5, 0),
            ItemTemplate = new FuncDataTemplate<OdContainerKind>((item, _) =>
                new TextBlock { Text = ContainerDisplayName(item) }),
        };

    private static string ContainerDisplayName(OdContainerKind value) =>
        value == OdContainerKind.Dictionary ? "Map" : value.ToString();

    private static StackPanel InspectorPanel(string title)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        return panel;
    }

    private void AddTextEditor(StackPanel panel, string label, string value, Action<string> commit, bool multiLine = false)
    {
        panel.Children.Add(SectionTitle(label));
        var editor = new TextBox
        {
            Text = value,
            AcceptsReturn = multiLine,
            MinHeight = multiLine ? 70 : 0,
            TextWrapping = multiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        CommitText(editor, value, commit);
        panel.Children.Add(editor);
    }

    private static void AddReadOnly(StackPanel panel, string label, string value)
    {
        panel.Children.Add(SectionTitle(label));
        panel.Children.Add(new TextBox { Text = value, IsReadOnly = true });
    }

    private static void AddCheckEditor(StackPanel panel, string label, bool value, Action<bool> commit)
    {
        var check = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 7, 0, 0) };
        check.IsCheckedChanged += (_, _) => commit(check.IsChecked == true);
        panel.Children.Add(check);
    }

    private static void AddFloatEditor(StackPanel panel, string label, float value, float minimum,
        Action<float> commit)
    {
        panel.Children.Add(SectionTitle(label));
        var editor = new NumericUpDown
        {
            Value = (decimal)value,
            Minimum = (decimal)minimum,
            Maximum = decimal.MaxValue,
            Increment = 1,
            FormatString = "0.##",
        };
        editor.ValueChanged += (_, _) =>
        {
            if (editor.Value is { } selected && (float)selected != value)
                commit((float)selected);
        };
        panel.Children.Add(editor);
    }

    private static void AddIntEditor(StackPanel panel, string label, int value, int minimum,
        Action<int> commit)
    {
        panel.Children.Add(SectionTitle(label));
        var editor = new NumericUpDown
        {
            Value = value,
            Minimum = minimum,
            Maximum = int.MaxValue,
            Increment = 1,
            FormatString = "0",
        };
        editor.ValueChanged += (_, _) =>
        {
            if (editor.Value is { } selected && decimal.ToInt32(selected) != value)
                commit(decimal.ToInt32(selected));
        };
        panel.Children.Add(editor);
    }

    private static void AddEnumEditor<T>(StackPanel panel, string label, T value, Action<T> commit)
        where T : struct, Enum
    {
        panel.Children.Add(SectionTitle(label));
        var editor = new ComboBox
        {
            ItemsSource = Enum.GetValues<T>(),
            SelectedItem = value,
        };
        editor.SelectionChanged += (_, _) =>
        {
            if (editor.SelectedItem is T selected && !EqualityComparer<T>.Default.Equals(selected, value))
                commit(selected);
        };
        panel.Children.Add(editor);
    }

    private static TextBlock SectionTitle(string text) =>
        new() { Text = text, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 7, 0, 1) };

    private static TextBlock SettingsGroupTitle(string text) =>
        new()
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 16, 0, 2),
        };

    private static Control EmptyMessage(string message) =>
        new TextBlock { Text = message, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    private static Control StatBlock(string label, object value) =>
        new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#333333")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(2, 10),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("180,*"),
                Children =
                {
                    new TextBlock { Text = label, Foreground = Brushes.Gray },
                    new TextBlock { Text = value.ToString(), [Grid.ColumnProperty] = 1 },
                },
            },
        };

    private static void CommitText(TextBox editor, string original, Action<string> commit)
    {
        var current = original;
        void TryCommit()
        {
            var value = editor.Text ?? string.Empty;
            if (string.Equals(value, current, StringComparison.Ordinal))
                return;
            current = value;
            commit(value);
        }
        editor.LostFocus += (_, _) => TryCommit();
        editor.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key == Key.Enter && !editor.AcceptsReturn)
            {
                TryCommit();
                eventArgs.Handled = true;
            }
        };
    }

    private static string NextName(IEnumerable<string> names, string prefix)
    {
        var used = names.ToHashSet(StringComparer.Ordinal);
        if (!used.Contains(prefix))
            return prefix;
        for (var index = 2; ; index++)
            if (!used.Contains(prefix + index))
                return prefix + index;
    }

    private OdScope? SelectedOd()
    {
        if (_session is null)
            return null;
        return _selectedEntity switch
        {
            OdScope scope => scope,
            OdDefinition definition => _session.Project.FindOd(definition),
            OdDataSet dataSet => _session.Project.FindOd(dataSet),
            _ => FindOwningDefinition(_selectedEntity) is { } owner
                ? _session.Project.FindOd(owner)
                : _session.Project.Ods.FirstOrDefault(),
        };
    }

    private OdDefinition? FindOwningDefinition(OdEntity? entity) =>
        entity is null || _session is null
            ? null
            : _session.Project.Definitions.FirstOrDefault(item =>
                OdProject.GetDefinitionChildren(item).Any(child => child.Id == entity.Id));

    private string NextOdNamespace(string name)
    {
        var project = _session!.Project;
        string root = project.DefaultNamespace.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? project.Name;
        string preferred = $"{root}.{name}";
        if (project.Ods.All(item => item.Namespace != preferred))
            return preferred;
        for (int index = 2; ; index++)
        {
            string candidate = preferred + index;
            if (project.Ods.All(item => item.Namespace != candidate))
                return candidate;
        }
    }

    private static string DefinitionIcon(OdDefinitionKind kind) => kind switch
    {
        OdDefinitionKind.Class => "C",
        OdDefinitionKind.Struct => "S",
        OdDefinitionKind.Resource => "R",
        OdDefinitionKind.SwitchStruct => "W",
        OdDefinitionKind.Enum => "E",
        OdDefinitionKind.Interface => "I",
        OdDefinitionKind.Event => "V",
        _ => "?",
    };

    private void RefreshCommandState()
    {
        bool editable = _session is not null && _browsedProject is null;
        UndoMenuItem.IsEnabled = editable && _session?.CanUndo == true;
        RedoMenuItem.IsEnabled = editable && _session?.CanRedo == true;
        SaveMenuItem.IsEnabled = _session is not null;
        AddMenuItem.IsEnabled = editable;
        WindowMenuItem.IsEnabled = true;
        ResourcesEditorMenuItem.IsEnabled = editable;
        SceneEditorMenuItem.IsEnabled = editable;
        ProjectMenuItem.IsEnabled = _session is not null;
        BuildMenuItem.IsEnabled = _session is not null;
        RefreshRunnerRunningState();
        GameplayMachineTab.IsEnabled = _session is not null;
        ValidateMenuItem.IsEnabled = _session is not null;
        ProjectSettingsMenuItem.IsEnabled = editable;
        RenameMenuItem.IsEnabled = editable && _selectedEntity is not null && _inheritedFieldSelection is null;
        DeleteMenuItem.IsEnabled = editable && _selectedEntity is not null && _inheritedFieldSelection is null;
        AddPackageReferenceButton.IsEnabled = _session is not null;
        RemovePackageReferenceButton.IsEnabled = editable && PackageReferencesList.SelectedItem is PackageReferenceItem;
    }

    private void SetDirty(bool value)
    {
        _dirty = value;
        string projectName = _session?.Project.Name ?? string.Empty;
        string title = string.IsNullOrEmpty(projectName)
            ? "GameplayMachine Editor"
            : $"{projectName} - GameplayMachine Editor";
        DirtyIndicator.Text = value ? "\u25CF" : string.Empty;
        WindowTitleText.Text = title;
        Title = value ? title + " *" : title;
    }

    private void SetStatus(string value) => StatusText.Text = value;

    private sealed record InheritedFieldSelection(Guid OwnerClassId, Guid SourceClassId, Guid FieldId);
    private sealed record DrivenFieldChoice(Guid Id, string Label)
    {
        public override string ToString() => Label;
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_dirty)
            return true;
        return await ShowConfirmAsync("Unsaved changes", "Discard the current unsaved changes?");
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90 };
        var dialog = Dialog(title, message, close);
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private async Task<string?> PromptForItemNameAsync(ExplorerAddKind addKind, Guid? ownerOdId)
    {
        string label = addKind switch
        {
            ExplorerAddKind.DataSet => "Data Set",
            ExplorerAddKind.SwitchStruct => "Switch Struct",
            ExplorerAddKind.PlayerInput => "Player Input",
            _ => addKind.ToString(),
        };
        string? result = null;
        var input = new TextBox { PlaceholderText = $"{label} name" };
        var error = new TextBlock
        {
            Foreground = Brushes.IndianRed,
            MinHeight = 34,
            TextWrapping = TextWrapping.Wrap,
        };
        var create = new Button { Content = "Create", MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, create },
        };
        var dialog = new Window
        {
            Title = $"Add {label}",
            Width = 430,
            Height = 220,
            MinWidth = 320,
            MinHeight = 160,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Name", FontWeight = FontWeight.SemiBold },
                    input,
                    error,
                    actions,
                },
            },
        };

        void TryCreate()
        {
            string name = input.Text?.Trim() ?? string.Empty;
            string? validationError = ValidateNewItemName(addKind, name, ownerOdId);
            if (validationError is not null)
            {
                error.Text = validationError;
                input.Focus();
                return;
            }
            result = name;
            dialog.Close();
        }

        create.Click += (_, _) => TryCreate();
        cancel.Click += (_, _) => dialog.Close();
        input.TextChanged += (_, _) => error.Text = string.Empty;
        input.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Key.Enter)
                return;
            TryCreate();
            eventArgs.Handled = true;
        };
        dialog.Opened += (_, _) => input.Focus();
        await dialog.ShowDialog(this);
        return result;
    }

    private async Task<string?> PromptForEntityNameAsync(OdEntity entity)
    {
        string? result = null;
        string kind = EntityKindLabel(entity);
        var input = new TextBox { Text = entity.Name };
        var error = new TextBlock
        {
            Foreground = Brushes.IndianRed,
            MinHeight = 34,
            TextWrapping = TextWrapping.Wrap,
        };
        var rename = new Button { Content = "Rename", MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, rename },
        };
        var dialog = new Window
        {
            Title = $"Rename {kind}",
            Width = 430,
            Height = 220,
            MinWidth = 320,
            MinHeight = 160,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Name", FontWeight = FontWeight.SemiBold },
                    input,
                    error,
                    actions,
                },
            },
        };

        void TryRename()
        {
            string name = input.Text?.Trim() ?? string.Empty;
            string? validationError = ValidateEntityName(entity, name);
            if (validationError is not null)
            {
                error.Text = validationError;
                input.Focus();
                return;
            }
            result = name;
            dialog.Close();
        }

        rename.Click += (_, _) => TryRename();
        cancel.Click += (_, _) => dialog.Close();
        input.TextChanged += (_, _) => error.Text = string.Empty;
        input.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Key.Enter)
                return;
            TryRename();
            eventArgs.Handled = true;
        };
        dialog.Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        await dialog.ShowDialog(this);
        return result;
    }

    private string? ValidateNewItemName(ExplorerAddKind addKind, string name, Guid? ownerOdId)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name is required.";
        if (!IsValidIdentifier(name))
            return "Name must start with a letter or underscore and contain only letters, digits, or underscores.";
        if (_session is null)
            return "No project is open.";

        OdScope? ownerOd = ownerOdId is { } id
            ? _session.Project.Ods.FirstOrDefault(item => item.Id == id)
            : SelectedOd();
        bool duplicate = addKind switch
        {
            ExplorerAddKind.Od => _session.Project.Ods.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)),
            ExplorerAddKind.DataSet => _session.Project.DataSets.Any(item =>
                _session.Project.FindOd(item)?.Id == ownerOd?.Id &&
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)),
            _ => _session.Project.Definitions.Any(item =>
                string.Equals(item.Namespace, ownerOd?.Namespace, StringComparison.Ordinal) &&
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)),
        };
        return duplicate ? $"An item named '{name}' already exists." : null;
    }

    private string? ValidateEntityName(OdEntity entity, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name is required.";
        if (!IsValidIdentifier(name))
            return "Name must start with a letter or underscore and contain only letters, digits, or underscores.";
        if (_session is null)
            return "No project is open.";

        bool duplicate = entity switch
        {
            OdScope scope => _session.Project.Ods.Any(item =>
                item.Id != scope.Id && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)),
            OdDefinition definition => _session.Project.Definitions.Any(item =>
                item.Id != definition.Id &&
                string.Equals(item.Namespace, definition.Namespace, StringComparison.Ordinal) &&
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)),
            OdDataSet dataSet => _session.Project.DataSets.Any(item =>
                item.Id != dataSet.Id && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)),
            OdDataRecord record => _session.Project.DataSets.Any(dataSet =>
                dataSet.Records.Any(item => item.Id == record.Id) && dataSet.Records.Any(item =>
                    item.Id != record.Id && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))),
            OdFieldDefinition field => _session.Project.Definitions.Any(definition =>
                OdProject.GetDefinitionChildren(definition).Any(item => item.Id == field.Id) &&
                OdProject.GetDefinitionChildren(definition).OfType<OdFieldDefinition>().Any(item =>
                    item.Id != field.Id && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))),
            OdEnumMember member => _session.Project.Definitions.OfType<OdEnumDefinition>().Any(definition =>
                definition.Members.Any(item => item.Id == member.Id) && definition.Members.Any(item =>
                    item.Id != member.Id && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))),
            _ => false,
        };
        return duplicate ? $"An item named '{name}' already exists in this scope." : null;
    }

    private static bool IsValidIdentifier(string name) =>
        name.Length > 0 &&
        (char.IsLetter(name[0]) || name[0] == '_') &&
        name.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');

    private static bool IsValidNamespace(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Split('.', StringSplitOptions.None).All(IsValidIdentifier);

    private async Task<bool> ShowConfirmAsync(string title, string message, string confirmLabel = "Discard")
    {
        var result = false;
        var yes = new Button { Content = confirmLabel, MinWidth = 90 };
        var no = new Button { Content = "Cancel", MinWidth = 90 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } };
        var dialog = Dialog(title, message, buttons);
        yes.Click += (_, _) => { result = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return result;
    }

    private static Window Dialog(string title, string message, Control actions) => new()
    {
        Title = title,
        Width = 430,
        Height = 180,
        MinWidth = 320,
        MinHeight = 140,
        CanResize = true,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 18,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, actions },
        },
    };

    private void OnWindowKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control) && eventArgs.Key == Key.N)
        {
            _ = NewProjectAsync();
            eventArgs.Handled = true;
        }
        else if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control) && eventArgs.Key == Key.O)
        {
            _ = OpenProjectAsync();
            eventArgs.Handled = true;
        }
        else if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control) && eventArgs.Key == Key.S)
        {
            _ = SaveProjectAsync();
            eventArgs.Handled = true;
        }
        else if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control) && eventArgs.Key == Key.Z)
        {
            _session?.Undo();
            eventArgs.Handled = true;
        }
        else if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control) && eventArgs.Key == Key.Y)
        {
            _session?.Redo();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.F5 && !eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = RunProjectAsync(true);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.F5 && eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = RunProjectAsync(false);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.F2 && _selectedEntity is not null && _inheritedFieldSelection is null)
        {
            _ = RenameSelectedEntityAsync();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Delete && _selectedEntity is not null)
        {
            DeleteSelectedEntity();
            eventArgs.Handled = true;
        }
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_allowClose || !_dirty)
            return;
        eventArgs.Cancel = true;
        if (_closePromptOpen)
            return;
        _closePromptOpen = true;
        var discard = await ShowConfirmAsync("Unsaved changes", "Discard the current unsaved changes and close GameplayMachine Editor?");
        _closePromptOpen = false;
        if (!discard)
            return;
        _allowClose = true;
        Close();
    }
}
