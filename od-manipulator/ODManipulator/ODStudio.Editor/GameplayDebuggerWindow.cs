using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using GMCore;

namespace ODStudio.Editor;

internal sealed class GameplayDebuggerWindow : Window
{
    private readonly TextBox _endpoint = new() { Text = "127.0.0.1:7788", Width = 180 };
    private readonly TextBox _token = new() { PlaceholderText = "Token", Width = 150 };
    private readonly Button _connect = new() { Content = "Connect" };
    private readonly TextBlock _connectionState = new() { Text = "Disconnected", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _machineState = new() { Text = "No GameplayMachine connected", TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _objectSearch = new() { PlaceholderText = "Filter objects...", Margin = new Thickness(7, 4) };
    private readonly ListBox _objects = new();
    private readonly ComboBox _createType = new() { MinWidth = 150 };
    private readonly Button _create = new() { Content = "+", IsEnabled = false };
    private readonly Button _delete = new() { Content = "Delete", IsEnabled = false };
    private readonly StackPanel _inspector = new() { Spacing = 7, Margin = new Thickness(12) };
    private readonly ComboBox _interface = new() { MinWidth = 240 };
    private readonly TextBlock _playerControllerLabel = new() { Text = "Player Controller", Foreground = Brush("#CCCCCC") };
    private readonly ComboBox _playerController = new() { MinWidth = 240 };
    private readonly StackPanel _interfaceInputs = new() { Spacing = 6 };
    private readonly Button _execute = new() { Content = "Execute", IsEnabled = false };
    private readonly TextBlock _interfaceOutput = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#B5CEA8") };
    private readonly ObservableCollection<TraceRow> _traces = new();
    private readonly ListBox _traceList = new();
    private readonly TextBlock _traceDetails = new()
    {
        Text = "Select an execution to inspect input and output.",
        FontFamily = "Cascadia Mono, Consolas",
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(8, 5),
    };
    private readonly TextBlock _status = new() { Text = "Ready", VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<long, TraceRow> _traceBySpan = new();
    private readonly List<InputEditor> _currentInputs = new();
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Border _connectionFrame = new()
    {
        BorderBrush = new SolidColorBrush(Color.Parse("#333333")),
        BorderThickness = new Thickness(1),
    };

    private GameplayDebugClient? _client;
    private GameplayDebugMachineInfo? _machine;
    private GameplayDebugSchema? _schema;
    private ObjectRow? _selectedObject;
    private ObjectRow[] _allObjects = Array.Empty<ObjectRow>();
    private readonly SemaphoreSlim _refreshObjectsLock = new(1, 1);
    private int _objectSelectionVersion;
    private bool _refreshingChangedObject;
    private bool _refreshChangedObjectAgain;
    private Process? _ownedProcess;

    public GameplayDebuggerWindow()
    {
        Title = "GameplayMachine Debugger";
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://GameplayMachine.Editor/Assets/ODStudio.ico")));
        Width = 1500;
        Height = 900;
        MinWidth = 1050;
        MinHeight = 650;
        CanResize = true;
        EditorWindowChrome.ConfigureWindow(this);
        _connectionFrame.Child = BuildLayout();
        Content = _connectionFrame;
        ToolTip.SetTip(_create, "Create GameplayObject");

        _objects.Classes.Add("compactExplorer");
        _objects.ItemTemplate = new FuncDataTemplate<ObjectRow>((row, _) => BuildObjectRow(row));
        _objects.SelectionChanged += async (_, _) => await SelectObjectAsync();
        GameplayObjectReferenceDrag.Attach(_objects, item =>
            item is ObjectRow row ? DebugObjectReference(row.Info) : null);
        _interface.SelectionChanged += (_, _) => BuildInterfaceInputs();
        _playerController.SelectionChanged += (_, _) =>
        {
            if (_interface.SelectedItem is InterfaceRow row &&
                row.Info.InterfaceType == GameplayInterfaceType.PlayerInput)
                _execute.IsEnabled = CanControl && _playerController.SelectedItem is ObjectRow;
        };
        _objectSearch.TextChanged += (_, _) => ApplyObjectFilter();
        _traceList.SelectionChanged += (_, _) =>
            _traceDetails.Text = (_traceList.SelectedItem as TraceRow)?.Details ??
                                 "Select an execution to inspect input and output.";
        _connect.Click += async (_, _) => await ToggleConnectionAsync();
        _create.Click += async (_, _) => await CreateObjectAsync();
        _delete.Click += async (_, _) => await DeleteObjectAsync();
        _execute.Click += async (_, _) => await ExecuteInterfaceAsync();
        _statusTimer.Tick += async (_, _) => await RefreshMachineInfoAsync();
        Closed += (_, _) => Disconnect();
    }

    internal async Task ConnectAsync(string host, int port, string? token)
    {
        if (_client is not null)
            Disconnect();
        _endpoint.Text = $"{host}:{port}";
        _token.Text = token ?? string.Empty;
        await ToggleConnectionAsync();
        if (_client is null)
            throw new InvalidOperationException(_status.Text);
    }

    internal void OwnProcess(Process process)
    {
        _ownedProcess = process;
        Closed += (_, _) =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch (InvalidOperationException) { }
        };
    }

    private Control BuildLayout()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("44,38,*,22") };
        root.Children.Add(EditorWindowChrome.BuildTitleBar(this, "GameplayMachine Debugger"));

        var toolbar = new Border
        {
            Background = Brush("#252526"),
            BorderBrush = Brush("#333333"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Margin = new Thickness(10, 5),
                Children = { new TextBlock { Text = "Endpoint", VerticalAlignment = VerticalAlignment.Center }, _endpoint,
                    _token, _connect, _connectionState },
            },
        };
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("270,4,*,4,390"),
            RowDefinitions = new RowDefinitions("*,280"),
        };
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        var objectPanel = Panel("GAMEPLAY OBJECTS", new Grid
        {
            RowDefinitions = new RowDefinitions("34,*,34"),
            Children =
            {
                _objectSearch,
                _objects,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,30,Auto"), Margin = new Thickness(7, 4),
                    Children = { _createType, _create, _delete },
                },
            },
        });
        Grid.SetRow(_objects, 1);
        Grid.SetColumn(_create, 1);
        Grid.SetColumn(_delete, 2);
        Grid.SetRow(objectPanel, 0);
        Grid.SetRowSpan(objectPanel, 2);
        body.Children.Add(objectPanel);

        var inspectorScroll = new ScrollViewer { Content = _inspector, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var inspectorPanel = Panel("OBJECT INSPECTOR", inspectorScroll);
        Grid.SetColumn(inspectorPanel, 2);
        body.Children.Add(inspectorPanel);

        var rightTabs = new TabControl
        {
            ItemsSource = new object[]
            {
                new TabItem { Header = "MACHINE", Content = new ScrollViewer { Content = _machineState, Margin = new Thickness(12) } },
                new TabItem { Header = "INTERFACES", Content = BuildInterfacePanel() },
            },
        };
        var rightPanel = new Border { Background = Brush("#252526"), BorderBrush = Brush("#333333"), BorderThickness = new Thickness(1, 0, 0, 0), Child = rightTabs };
        Grid.SetColumn(rightPanel, 4);
        body.Children.Add(rightPanel);

        var splitter1 = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter1, 1);
        Grid.SetRowSpan(splitter1, 2);
        body.Children.Add(splitter1);
        var splitter2 = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter2, 3);
        Grid.SetRowSpan(splitter2, 2);
        body.Children.Add(splitter2);

        _traceList.ItemsSource = _traces;
        _traceList.ItemTemplate = new FuncDataTemplate<TraceRow>((row, _) => new TextBlock
        {
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(TraceRow.Text)),
            FontFamily = "Cascadia Mono, Consolas",
            FontSize = 11,
            Margin = new Thickness(5, 2),
        });
        var traceContent = new Grid { RowDefinitions = new RowDefinitions("*,100") };
        traceContent.Children.Add(_traceList);
        var traceDetailsBorder = new Border
        {
            BorderBrush = Brush("#333333"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new ScrollViewer { Content = _traceDetails },
        };
        Grid.SetRow(traceDetailsBorder, 1);
        traceContent.Children.Add(traceDetailsBorder);
        var tracePanel = Panel("EXECUTION LOG", traceContent);
        Grid.SetColumn(tracePanel, 2);
        Grid.SetColumnSpan(tracePanel, 3);
        Grid.SetRow(tracePanel, 1);
        body.Children.Add(tracePanel);

        var statusBorder = new Border { Background = Brush("#007ACC"), Child = _status, Padding = new Thickness(8, 0) };
        Grid.SetRow(statusBorder, 3);
        root.Children.Add(statusBorder);
        return root;
    }

    private Control BuildInterfacePanel()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
        panel.Children.Add(_interface);
        panel.Children.Add(_playerControllerLabel);
        panel.Children.Add(_playerController);
        panel.Children.Add(_interfaceInputs);
        panel.Children.Add(_execute);
        panel.Children.Add(new TextBlock { Text = "RESULT", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        panel.Children.Add(_interfaceOutput);
        return new ScrollViewer { Content = panel };
    }

    private async Task ToggleConnectionAsync()
    {
        if (_client is not null)
        {
            Disconnect();
            return;
        }
        string[] endpoint = (_endpoint.Text ?? string.Empty).Split(':');
        if (endpoint.Length != 2 || !int.TryParse(endpoint[1], out int port))
        {
            SetStatus("Endpoint must use host:port format", true);
            return;
        }
        try
        {
            _connect.IsEnabled = false;
            _connectionState.Text = "Connecting...";
            var client = new GameplayDebugClient();
            _client = client;
            client.TraceReceived += trace => Dispatcher.UIThread.Post(() => AddTrace(trace));
            client.ObjectChanged += (id, kind) =>
                Dispatcher.UIThread.Post(async () => await OnObjectChangedAsync(id, kind));
            client.Disconnected += error => Dispatcher.UIThread.Post(() => OnDisconnected(error));
            _machine = await client.ConnectAsync(endpoint[0], port, string.IsNullOrEmpty(_token.Text) ? null : _token.Text);
            _schema = await client.GetSchemaAsync();
            ClassRow[] creatableClasses = _schema.Classes.Where(item => !item.IsEngineManaged)
                .Select(item => new ClassRow(item)).ToArray();
            _createType.ItemsSource = creatableClasses;
            _interface.ItemsSource = _schema.Interfaces.Select(item => new InterfaceRow(item)).ToArray();
            _createType.SelectedIndex = creatableClasses.Length > 0 ? 0 : -1;
            _interface.SelectedIndex = _schema.Interfaces.Count > 0 ? 0 : -1;
            _connect.Content = "Disconnect";
            _connectionState.Text = $"{_machine.Role} / {_machine.NetworkState}";
            _connectionFrame.BorderBrush = Brush("#F14C4C");
            _connectionFrame.BorderThickness = new Thickness(2);
            _create.IsEnabled = CanControlObjects;
            await RefreshObjectsAsync();
            ShowMachineInfo();
            _statusTimer.Start();
            SetStatus("Connected");
        }
        catch (Exception exception)
        {
            Disconnect();
            SetStatus(exception.Message, true);
        }
        finally
        {
            _connect.IsEnabled = true;
        }
    }

    private async Task RefreshObjectsAsync()
    {
        await _refreshObjectsLock.WaitAsync();
        try
        {
            GameplayDebugClient? client = _client;
            if (client is null) return;
            int selectedID = _selectedObject?.Info.ObjectID ?? 0;
            IReadOnlyList<GameplayDebugObjectInfo> objects = await client.GetObjectsAsync();
            if (!ReferenceEquals(client, _client)) return;
            _allObjects = objects.OrderBy(item => item.ClassName).ThenBy(item => item.ObjectID)
                .Select(item => new ObjectRow(item)).ToArray();
            int selectedPlayerID = (_playerController.SelectedItem as ObjectRow)?.Info.ObjectID ?? 0;
            ulong stateSyncPlayerControllerID = ODSaveHash.Compute("GMCore.StateSync.PlayerController");
            ulong lockstepPlayerControllerID = ODSaveHash.Compute("GMCore.Lockstep.PlayerController");
            ObjectRow[] playerControllers = _allObjects
                .Where(item => item.Info.ClassID == stateSyncPlayerControllerID ||
                               item.Info.ClassID == lockstepPlayerControllerID)
                .ToArray();
            _playerController.ItemsSource = playerControllers;
            _playerController.SelectedItem = playerControllers.FirstOrDefault(item => item.Info.ObjectID == selectedPlayerID)
                                             ?? playerControllers.FirstOrDefault();
            ApplyObjectFilter();
            BuildInterfaceInputs();
            if (selectedID != 0)
                _objects.SelectedItem = (_objects.ItemsSource as IEnumerable<ObjectRow>)?
                    .FirstOrDefault(item => item.Info.ObjectID == selectedID);
        }
        catch (Exception exception) { SetStatus(exception.Message, true); }
        finally { _refreshObjectsLock.Release(); }
    }

    private async Task SelectObjectAsync()
    {
        int version = ++_objectSelectionVersion;
        _selectedObject = _objects.SelectedItem as ObjectRow;
        _delete.IsEnabled = _selectedObject is not null && CanControlObjects;
        _inspector.Children.Clear();
        if (_selectedObject is null || _client is null || _schema is null) return;
        int objectID = _selectedObject.Info.ObjectID;
        GameplayDebugClient client = _client;
        GameplayDebugSchema schema = _schema;
        try
        {
            GameplayDebugObjectSnapshot snapshot = await client.GetObjectAsync(objectID);
            if (version != _objectSelectionVersion || !ReferenceEquals(client, _client) ||
                _selectedObject?.Info.ObjectID != objectID)
                return;

            _inspector.Children.Clear();
            GameplayDebugClassInfo? classInfo = schema.Classes.FirstOrDefault(item => item.StableID == snapshot.Object.ClassID);
            _inspector.Children.Add(new TextBlock
            {
                Text = $"{snapshot.Object.ClassName}   ID {snapshot.Object.ObjectID}{(snapshot.Object.IsRoot ? "   ROOT" : string.Empty)}",
                FontSize = 16, FontWeight = FontWeight.SemiBold,
            });
            if (classInfo is null) return;
            foreach (GameplayDebugFieldValue fieldValue in snapshot.Fields)
            {
                GameplayDebugFieldInfo? field = classInfo.Fields.FirstOrDefault(item => item.StableID == fieldValue.FieldID);
                if (field is not null) _inspector.Children.Add(BuildFieldEditor(snapshot.Object.ObjectID, field, fieldValue.Value));
            }
        }
        catch (Exception exception) { SetStatus(exception.Message, true); }
    }

    private Control BuildFieldEditor(int objectID, GameplayDebugFieldInfo field, GameplayDebugValue value)
    {
        var editor = new DebugValueEditor(value, field, DebugObjectReferences());
        editor.Control.IsEnabled = !field.IsDriven && CanControlObjects;
        var apply = new Button { Content = "Apply", IsEnabled = !field.IsDriven && CanControlObjects };
        apply.Click += async (_, _) =>
        {
            if (_client is null) return;
            try
            {
                await _client.SetFieldAsync(objectID, field.StableID, editor.Value);
                SetStatus($"Updated {field.Name}");
            }
            catch (Exception exception) { SetStatus(exception.Message, true); }
        };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        header.Children.Add(new TextBlock { Text = field.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var type = new TextBlock { Text = field.IsDriven ? $"{field.TypeName}  driven" : field.TypeName, Foreground = Brush("#969696"), Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(type, 1); header.Children.Add(type);
        Grid.SetColumn(apply, 2); header.Children.Add(apply);
        var panel = new StackPanel { Spacing = 5 };
        panel.Children.Add(header);
        panel.Children.Add(editor.Control);
        return new Border { BorderBrush = Brush("#3C3C3C"), BorderThickness = new Thickness(1), Padding = new Thickness(8), Child = panel };
    }

    private void BuildInterfaceInputs()
    {
        _interfaceInputs.Children.Clear();
        _currentInputs.Clear();
        if (_interface.SelectedItem is not InterfaceRow row)
        {
            _playerControllerLabel.IsVisible = _playerController.IsVisible = false;
            _execute.IsEnabled = false;
            return;
        }
        bool isPlayerInput = row.Info.InterfaceType == GameplayInterfaceType.PlayerInput;
        _playerControllerLabel.IsVisible = _playerController.IsVisible = isPlayerInput;
        foreach (GameplayDebugFieldInfo field in row.Info.Inputs)
        {
            GameplayDebugValue initial = field.DefaultValue ?? DebugValueEditor.DefaultValue(field);
            var editor = new DebugValueEditor(initial, field, DebugObjectReferences());
            _currentInputs.Add(new InputEditor(field.Name, editor));
            _interfaceInputs.Children.Add(new TextBlock { Text = field.Name, Foreground = Brush("#CCCCCC") });
            _interfaceInputs.Children.Add(editor.Control);
        }
        _execute.IsEnabled = CanControl &&
                             (!isPlayerInput || _playerController.SelectedItem is ObjectRow) &&
                             !(row.Info.RpcMode == GameplayRpcMode.Multicast &&
                               _machine?.Role == GameplayMachineRole.ClientProxy);
    }

    private async Task ExecuteInterfaceAsync()
    {
        if (_client is null || _interface.SelectedItem is not InterfaceRow row) return;
        try
        {
            var inputs = _currentInputs.ToDictionary(item => item.Name, item => item.Editor.Value, StringComparer.Ordinal);
            int playerControllerID = row.Info.InterfaceType == GameplayInterfaceType.PlayerInput
                ? (_playerController.SelectedItem as ObjectRow)?.Info.ObjectID ?? 0
                : 0;
            ODDebugInvocationResult result = await _client.InvokeAsync(row.Info.StableID, inputs, playerControllerID);
            _interfaceOutput.Text = result.Succeeded ? DebugValueEditor.Format(result.Output) : result.Error;
            SetStatus(result.Succeeded ? $"Executed {row.Info.Name}" : result.Error ?? "Execution failed", !result.Succeeded);
        }
        catch (Exception exception) { SetStatus(exception.Message, true); }
    }

    private async Task CreateObjectAsync()
    {
        if (_client is null || _createType.SelectedItem is not ClassRow row) return;
        try
        {
            int id = await _client.CreateObjectAsync(row.Info.StableID);
            await RefreshObjectsAsync();
            _objects.SelectedItem = (_objects.ItemsSource as IEnumerable<ObjectRow>)?.FirstOrDefault(item => item.Info.ObjectID == id);
            SetStatus($"Created {row.Info.Name} #{id}");
        }
        catch (Exception exception) { SetStatus(exception.Message, true); }
    }

    private async Task DeleteObjectAsync()
    {
        if (_client is null || _selectedObject is null) return;
        try
        {
            int id = _selectedObject.Info.ObjectID;
            await _client.DeleteObjectAsync(id);
            _selectedObject = null;
            _inspector.Children.Clear();
            await RefreshObjectsAsync();
            SetStatus($"Deleted GameplayObject #{id}");
        }
        catch (Exception exception) { SetStatus(exception.Message, true); }
    }

    private async Task OnObjectChangedAsync(int objectID, GameplayDebugObjectChangeKind changeKind)
    {
        if (changeKind != GameplayDebugObjectChangeKind.FieldChanged)
        {
            await RefreshObjectsAsync();
            return;
        }
        if (_selectedObject?.Info.ObjectID != objectID)
            return;
        if (_refreshingChangedObject)
        {
            _refreshChangedObjectAgain = true;
            return;
        }

        _refreshingChangedObject = true;
        try
        {
            do
            {
                _refreshChangedObjectAgain = false;
                await SelectObjectAsync();
            } while (_refreshChangedObjectAgain && _selectedObject?.Info.ObjectID == objectID);
        }
        finally
        {
            _refreshingChangedObject = false;
        }
    }

    private void AddTrace(GameplayExecutionTraceEvent trace)
    {
        if (trace.Stage == GameplayExecutionTraceStage.Started)
        {
            var row = new TraceRow(trace);
            _traceBySpan[trace.SpanID] = row;
            _traces.Add(row);
            if (_traces.Count > 3000)
            {
                TraceRow expired = _traces[0];
                _traces.RemoveAt(0);
                _traceBySpan.Remove(expired.SpanID);
            }
        }
        else if (_traceBySpan.TryGetValue(trace.SpanID, out TraceRow? row))
        {
            row.Complete(trace);
            if (ReferenceEquals(_traceList.SelectedItem, row))
                _traceDetails.Text = row.Details;
        }
    }

    private async Task RefreshMachineInfoAsync()
    {
        if (_client is null) return;
        try
        {
            _machine = await _client.GetMachineInfoAsync();
            ShowMachineInfo();
        }
        catch { }
    }

    private void ShowMachineInfo()
    {
        if (_machine is null) return;
        _connectionState.Text = $"{_machine.Role} / {_machine.NetworkState}";
        _machineState.Text =
            $"Debug access: {_machine.Access}\nRole: {_machine.Role}\nNetwork: {_machine.NetworkState}\nRevision: {_machine.NetworkRevision}\n" +
            $"Objects: {_machine.ObjectCount}\nRoot: {(_machine.RootObjectID == 0 ? "None" : _machine.RootObjectID)}\n" +
            $"Executing: {_machine.DuringExecution}\nRoutine: {_machine.HasRoutine}\n\n" +
            $"Network Schema: {_machine.NetworkSchemaID}\nResource Catalog: {_machine.ResourceCatalogID}\n" +
            $"Debug Schema: {_machine.DebugSchemaID}\n\nModules\n{string.Join("\n", _machine.Modules)}";
    }

    private void OnDisconnected(Exception? error)
    {
        if (_client is null) return;
        Disconnect();
        SetStatus(error?.Message ?? "Disconnected", error is not null);
    }

    private void Disconnect()
    {
        _statusTimer.Stop();
        _client?.Dispose();
        _client = null;
        _machine = null;
        _schema = null;
        _allObjects = Array.Empty<ObjectRow>();
        _objects.ItemsSource = null;
        _createType.ItemsSource = null;
        _interface.ItemsSource = null;
        _playerController.ItemsSource = null;
        _playerControllerLabel.IsVisible = _playerController.IsVisible = false;
        _inspector.Children.Clear();
        _connect.Content = "Connect";
        _connectionState.Text = "Disconnected";
        _machineState.Text = "No GameplayMachine connected";
        _connectionFrame.BorderBrush = Brush("#333333");
        _connectionFrame.BorderThickness = new Thickness(1);
        _create.IsEnabled = _delete.IsEnabled = _execute.IsEnabled = false;
    }

    private bool CanControl => _client is not null && _machine?.Access == GameplayDebugAccess.Control;
    private bool CanControlObjects => CanControl && _machine?.Role == GameplayMachineRole.Authority;

    private void ApplyObjectFilter()
    {
        string filter = (_objectSearch.Text ?? string.Empty).Trim();
        ObjectRow[] rows = filter.Length == 0
            ? _allObjects
            : _allObjects.Where(item => item.Info.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                        item.Info.ObjectID.ToString(CultureInfo.InvariantCulture).Contains(filter,
                                            StringComparison.OrdinalIgnoreCase)).ToArray();
        _objects.ItemsSource = rows;
    }

    private Control BuildObjectRow(ObjectRow? row)
    {
        if (row is null)
            return new TextBlock();
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*"), MinHeight = 20 };
        content.Children.Add(new TextBlock
        {
            Text = row.Info.IsRoot ? "◆" : "•",
            FontSize = row.Info.IsRoot ? 9 : 11,
            Foreground = Brush(row.Info.IsRoot ? "#E2C08D" : "#4EC9B0"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var label = new TextBlock
        {
            Text = $"{row.Info.ClassName}  #{row.Info.ObjectID}",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(label, 1);
        content.Children.Add(label);
        return content;
    }

    private IReadOnlyList<GameplayObjectReferenceOption> DebugObjectReferences() =>
        _allObjects.Select(item => DebugObjectReference(item.Info)).ToArray();

    private GameplayObjectReferenceOption DebugObjectReference(GameplayDebugObjectInfo info)
    {
        GameplayDebugClassInfo? actual = _schema?.Classes.FirstOrDefault(item => item.StableID == info.ClassID);
        IReadOnlySet<string> assignable = (actual?.AssignableClassIDs.Count > 0
                ? actual.AssignableClassIDs
                : new List<ulong> { info.ClassID })
            .Select(item => item.ToString(CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.Ordinal);
        return new GameplayObjectReferenceOption(info.ObjectID.ToString(CultureInfo.InvariantCulture),
            $"{info.ClassName} #{info.ObjectID}", info.ClassName,
            info.ClassID.ToString(CultureInfo.InvariantCulture), assignable);
    }

    private void SetStatus(string message, bool error = false)
    {
        _status.Text = message;
        _status.Foreground = error ? Brush("#FFFFFF") : Brush("#FFFFFF");
    }

    private static Border Panel(string title, Control content)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("32,*") };
        grid.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brush("#969696"), Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(content, 1); grid.Children.Add(content);
        return new Border { Background = Brush("#1E1E1E"), BorderBrush = Brush("#333333"), BorderThickness = new Thickness(0, 0, 1, 1), Child = grid };
    }

    private static SolidColorBrush Brush(string value) => new(Color.Parse(value));

    private sealed record ObjectRow(GameplayDebugObjectInfo Info)
    {
        public override string ToString() => $"{(Info.IsRoot ? "◆ " : string.Empty)}{Info.ClassName}  #{Info.ObjectID}";
    }

    private sealed record ClassRow(GameplayDebugClassInfo Info)
    {
        public override string ToString() => Info.Name;
    }

    private sealed record InterfaceRow(GameplayDebugInterfaceInfo Info)
    {
        public override string ToString() => $"{Info.Name}  [{Info.InterfaceType} / {Info.RpcMode}]";
    }

    private sealed record InputEditor(string Name, DebugValueEditor Editor);

    private sealed class TraceRow : INotifyPropertyChanged
    {
        private string _text;
        public string Text { get => _text; private set { _text = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text))); } }
        private string _details;
        public string Details { get => _details; private set { _details = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Details))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private readonly GameplayExecutionTraceEvent _start;
        public long SpanID => _start.SpanID;

        public TraceRow(GameplayExecutionTraceEvent start)
        {
            _start = start;
            _text = Prefix(start) + $"▶ {start.InterfaceName}  [{start.RpcMode}]";
            _details = BuildDetails(start, null);
        }

        public void Complete(GameplayExecutionTraceEvent completed)
        {
            Text = Prefix(completed) + $"{(completed.Succeeded ? "✓" : "✕")} {completed.InterfaceName}  {completed.DurationMilliseconds:0.###} ms" +
                   (string.IsNullOrWhiteSpace(completed.Error)
                       ? string.Empty
                       : $"  Failed: {FirstLine(completed.Error)}");
            Details = BuildDetails(_start, completed);
        }

        private static string Prefix(GameplayExecutionTraceEvent value) => new string(' ', value.Depth * 3) + (value.Depth == 0 ? string.Empty : "└─ ");

        private static string FirstLine(string value)
        {
            int lineBreak = value.IndexOfAny(['\r', '\n']);
            return lineBreak < 0 ? value : value[..lineBreak];
        }

        private static string BuildDetails(GameplayExecutionTraceEvent started, GameplayExecutionTraceEvent? completed)
        {
            string result = $"{started.InterfaceName} [{started.RpcMode}]\n" +
                            $"Origin: {started.Origin}  Trace: {started.TraceID}  Span: {started.SpanID}  Parent: {started.ParentSpanID}\n" +
                            $"Input: {DebugValueEditor.Format(started.Input)}";
            if (completed is null) return result + "\nStatus: running";
            result += $"\nOutput: {DebugValueEditor.Format(completed.Output)}\n" +
                      $"Status: {(completed.Succeeded ? "succeeded" : "failed")}  Duration: {completed.DurationMilliseconds:0.###} ms";
            if (!string.IsNullOrWhiteSpace(completed.Error)) result += $"\nError: {completed.Error}";
            return result;
        }
    }
}

internal sealed class DebugValueEditor
{
    private readonly GameplayDebugValue _source;
    private readonly GameplayDebugFieldInfo _field;
    private readonly TextBox? _text;
    private readonly CheckBox? _check;
    private readonly GameplayObjectReferenceEditor? _objectReference;
    private readonly Control? _resourceReference;
    private string? _selectedResourceId;
    private readonly IReadOnlyList<GameplayObjectReferenceOption> _objectReferences;
    private readonly List<DebugValueEditor> _items = new();
    private readonly List<(DebugValueEditor Key, DebugValueEditor Value)> _entries = new();
    private readonly List<(string Name, DebugValueEditor Editor)> _members = new();

    public Control Control { get; }

    public DebugValueEditor(GameplayDebugValue source, GameplayDebugFieldInfo field,
        IReadOnlyList<GameplayObjectReferenceOption>? objectReferences = null)
    {
        _source = source ?? GameplayDebugValue.Null(field.TypeName);
        _field = field;
        _objectReferences = objectReferences ?? Array.Empty<GameplayObjectReferenceOption>();
        if (_field.IsResourceReference || _source.Kind == GameplayDebugValueKind.Resource)
        {
            _selectedResourceId = _source.Scalar;
            _resourceReference = ResourceReferencePicker.Build(
                _field.ResourceOptions.Select(item => DebugResourceOption(item, _field.TypeName)).ToList(),
                _selectedResourceId,
                selected => _selectedResourceId = selected);
            Control = _resourceReference;
        }
        else if (_field.IsGameplayObjectReference || _source.Kind == GameplayDebugValueKind.GameplayObject)
        {
            string requiredTypeKey = _field.GameplayObjectClassID == 0
                ? string.Empty
                : _field.GameplayObjectClassID.ToString(CultureInfo.InvariantCulture);
            _objectReference = new GameplayObjectReferenceEditor(_objectReferences,
                _source.ObjectID == 0 ? null : _source.ObjectID.ToString(CultureInfo.InvariantCulture),
                requiredTypeKey, ShortTypeName(_field.TypeName), _ => { });
            Control = _objectReference;
        }
        else if (_source.Kind == GameplayDebugValueKind.Boolean)
        {
            _check = new CheckBox { IsChecked = string.Equals(_source.Scalar, "true", StringComparison.OrdinalIgnoreCase) };
            Control = _check;
        }
        else if (_source.Kind == GameplayDebugValueKind.Sequence || field.Container is GameplayDebugContainerKind.List or GameplayDebugContainerKind.Set)
        {
            Control = BuildSequence();
        }
        else if (_source.Kind == GameplayDebugValueKind.Map || field.Container == GameplayDebugContainerKind.Map)
        {
            Control = BuildMap();
        }
        else if (_source.Kind == GameplayDebugValueKind.Object)
        {
            Control = BuildObject();
        }
        else
        {
            _text = new TextBox { Text = _source.Scalar ?? string.Empty };
            Control = _text;
        }
    }

    public GameplayDebugValue Value
    {
        get
        {
            if (_field.IsResourceReference || _source.Kind == GameplayDebugValueKind.Resource)
                return string.IsNullOrWhiteSpace(_selectedResourceId)
                    ? GameplayDebugValue.Null(_field.TypeName)
                    : new GameplayDebugValue { Kind = GameplayDebugValueKind.Resource, TypeName = _field.TypeName, Scalar = _selectedResourceId };
            if (_field.IsGameplayObjectReference || _source.Kind == GameplayDebugValueKind.GameplayObject)
                return new GameplayDebugValue { Kind = GameplayDebugValueKind.GameplayObject, TypeName = _field.TypeName,
                    ObjectID = int.TryParse(_objectReference?.Selected?.Key, out int id) ? id : 0 };
            if (_check is not null)
                return new GameplayDebugValue { Kind = GameplayDebugValueKind.Boolean, TypeName = _field.TypeName, Scalar = _check.IsChecked == true ? "true" : "false" };
            if (_source.Kind == GameplayDebugValueKind.Sequence || _field.Container is GameplayDebugContainerKind.List or GameplayDebugContainerKind.Set)
                return new GameplayDebugValue { Kind = GameplayDebugValueKind.Sequence, TypeName = _source.TypeName, Items = _items.Select(item => item.Value).ToList() };
            if (_source.Kind == GameplayDebugValueKind.Map || _field.Container == GameplayDebugContainerKind.Map)
                return new GameplayDebugValue { Kind = GameplayDebugValueKind.Map, TypeName = _source.TypeName,
                    Entries = _entries.Select(item => new GameplayDebugMapEntry { Key = item.Key.Value, Value = item.Value.Value }).ToList() };
            if (_source.Kind == GameplayDebugValueKind.Object)
                return new GameplayDebugValue { Kind = GameplayDebugValueKind.Object, TypeName = _source.TypeName,
                    Members = _members.Select(item => new GameplayDebugNamedValue { Name = item.Name, Value = item.Editor.Value }).ToList() };
            return new GameplayDebugValue { Kind = _source.Kind == GameplayDebugValueKind.Null ? KindFor(_field.TypeName) : _source.Kind,
                TypeName = _field.TypeName, Scalar = _text?.Text ?? string.Empty };
        }
    }

    private Control BuildSequence()
    {
        var stack = new StackPanel { Spacing = 4 };
        foreach (GameplayDebugValue item in _source.Items)
            AddSequenceRow(stack, item);
        var add = new Button { Content = "+ Item", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) =>
            AddSequenceRow(stack, DefaultElementValue(), stack.Children.Count - 1);
        stack.Children.Add(add);
        return stack;
    }

    private void AddSequenceRow(StackPanel stack, GameplayDebugValue value, int? index = null)
    {
        var child = new DebugValueEditor(value, ElementField(), _objectReferences);
        var remove = new Button { Content = "×", Width = 28 };
        ToolTip.SetTip(remove, "Remove item");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,30"), Children = { child.Control, remove } };
        Grid.SetColumn(remove, 1);
        remove.Click += (_, _) =>
        {
            _items.Remove(child);
            stack.Children.Remove(row);
        };
        _items.Add(child);
        if (index.HasValue) stack.Children.Insert(index.Value, row);
        else stack.Children.Add(row);
    }

    private Control BuildMap()
    {
        var stack = new StackPanel { Spacing = 5 };
        foreach (GameplayDebugMapEntry entry in _source.Entries)
            AddMapRow(stack, entry.Key, entry.Value);
        var add = new Button { Content = "+ Entry", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) =>
            AddMapRow(stack, DefaultValue(KeyField()), DefaultElementValue(), stack.Children.Count - 1);
        stack.Children.Add(add);
        return stack;
    }

    private void AddMapRow(StackPanel stack, GameplayDebugValue keyValue, GameplayDebugValue itemValue,
        int? index = null)
    {
        var key = new DebugValueEditor(keyValue, KeyField(), _objectReferences);
        var value = new DebugValueEditor(itemValue, ElementField(), _objectReferences);
        var remove = new Button { Content = "×", Width = 28 };
        ToolTip.SetTip(remove, "Remove entry");
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,30"),
            Children = { key.Control, value.Control, remove },
        };
        Grid.SetColumn(value.Control, 1);
        Grid.SetColumn(remove, 2);
        remove.Click += (_, _) =>
        {
            _entries.Remove((key, value));
            stack.Children.Remove(row);
        };
        _entries.Add((key, value));
        if (index.HasValue) stack.Children.Insert(index.Value, row);
        else stack.Children.Add(row);
    }

    private Control BuildObject()
    {
        var stack = new StackPanel { Spacing = 5 };
        foreach (GameplayDebugNamedValue member in _source.Members)
        {
            var info = new GameplayDebugFieldInfo { Name = member.Name, TypeName = member.Value.TypeName ?? member.Name };
            var editor = new DebugValueEditor(member.Value, info, _objectReferences);
            _members.Add((member.Name, editor));
            stack.Children.Add(new TextBlock { Text = member.Name, Foreground = new SolidColorBrush(Color.Parse("#969696")) });
            stack.Children.Add(editor.Control);
        }
        return stack;
    }

    private GameplayDebugFieldInfo ElementField() => new()
    {
        Name = "Item", TypeName = _field.TypeName, IsGameplayObjectReference = _field.IsGameplayObjectReference,
        GameplayObjectClassID = _field.GameplayObjectClassID,
        IsResourceReference = _field.IsResourceReference,
        ResourceOptions = _field.ResourceOptions,
        DefaultValue = _field.DefaultValue,
    };

    private GameplayDebugFieldInfo KeyField() => new()
    {
        Name = "Key",
        TypeName = _field.KeyTypeName ?? "System.String",
        IsResourceReference = _field.IsResourceKeyReference,
        ResourceOptions = _field.ResourceKeyOptions,
    };
    private GameplayDebugValue DefaultElementValue() => DefaultValue(ElementField());

    public static GameplayDebugValue DefaultValue(GameplayDebugFieldInfo field)
    {
        if (field.DefaultValue is not null) return field.DefaultValue;
        if (field.IsResourceReference) return new GameplayDebugValue { Kind = GameplayDebugValueKind.Resource, TypeName = field.TypeName };
        if (field.IsGameplayObjectReference) return new GameplayDebugValue { Kind = GameplayDebugValueKind.GameplayObject, TypeName = field.TypeName };
        if (field.Container is GameplayDebugContainerKind.List or GameplayDebugContainerKind.Set)
            return new GameplayDebugValue { Kind = GameplayDebugValueKind.Sequence, TypeName = field.TypeName };
        if (field.Container == GameplayDebugContainerKind.Map)
            return new GameplayDebugValue { Kind = GameplayDebugValueKind.Map, TypeName = field.TypeName };
        return new GameplayDebugValue { Kind = KindFor(field.TypeName), TypeName = field.TypeName,
            Scalar = field.TypeName == "System.Boolean" ? "false" : string.Empty };
    }

    private static GameplayDebugValueKind KindFor(string? typeName)
    {
        if (typeName == "System.Boolean") return GameplayDebugValueKind.Boolean;
        if (typeName is "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64") return GameplayDebugValueKind.Integer;
        if (typeName is "System.Single" or "System.Double") return GameplayDebugValueKind.FloatingPoint;
        if (typeName == "System.Decimal") return GameplayDebugValueKind.Decimal;
        if (typeName == "System.DateTime") return GameplayDebugValueKind.DateTime;
        return GameplayDebugValueKind.String;
    }

    private static string ShortTypeName(string? typeName) =>
        string.IsNullOrWhiteSpace(typeName) ? "GameplayObject" : typeName.Split('.').Last();

    private static ResourceReferenceOption DebugResourceOption(GameplayDebugResourceOption item, string? typeName)
    {
        int separator = item.Name.IndexOf(" / ", StringComparison.Ordinal);
        return separator < 0
            ? new ResourceReferenceOption(item.ID, item.Name, string.Empty, ShortTypeName(typeName))
            : new ResourceReferenceOption(item.ID, item.Name[(separator + 3)..], item.Name[..separator], ShortTypeName(typeName));
    }

    public static string Format(GameplayDebugValue? value, int depth = 0)
    {
        if (value is null || value.Kind == GameplayDebugValueKind.Null) return "null";
        if (value.Kind == GameplayDebugValueKind.GameplayObject) return $"GameplayObject #{value.ObjectID}";
        if (value.Kind == GameplayDebugValueKind.Resource) return $"Resource {value.Scalar}";
        if (value.Kind == GameplayDebugValueKind.Sequence) return "[" + string.Join(", ", value.Items.Select(item => Format(item, depth + 1))) + "]";
        if (value.Kind == GameplayDebugValueKind.Map) return "{" + string.Join(", ", value.Entries.Select(item => $"{Format(item.Key)}: {Format(item.Value)}")) + "}";
        if (value.Kind == GameplayDebugValueKind.Object) return "{\n" + string.Join(",\n", value.Members.Select(item => $"  {item.Name}: {Format(item.Value, depth + 1)}")) + "\n}";
        return value.Scalar ?? string.Empty;
    }
}
