using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using ODStudio.Model;

namespace ODStudio.Editor;

internal abstract class ProjectDataEditorWindow : Window
{
    private readonly OdProjectSession _session;
    private readonly Func<Task> _save;
    private readonly Func<IEnumerable<OdDefinition>> _referencedDefinitions;
    private readonly EditorDataMode _mode;
    private readonly ListBox _tree = new();
    private readonly TextBox _search = new() { PlaceholderText = "Filter..." };
    private readonly ContentControl _editor = new();
    private readonly TextBlock _documentTitle = new() { Text = "Nothing selected", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Text = "Ready", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _addRoot = ExplorerIconButton(
        "M11 5 L13 5 L13 11 L19 11 L19 13 L13 13 L13 19 L11 19 L11 13 L5 13 L5 11 L11 11 Z",
        "Add");
    private Guid? _selectedId;
    private bool _refreshing;

    protected ProjectDataEditorWindow(OdProjectSession session, Func<Task> save,
        Func<IEnumerable<OdDefinition>> referencedDefinitions, EditorDataMode mode)
    {
        _session = session;
        _save = save;
        _referencedDefinitions = referencedDefinitions;
        _mode = mode;
        Title = mode == EditorDataMode.Resources ? "Resources Editor" : "Scene Editor";
        Width = 1280;
        Height = 820;
        MinWidth = 900;
        MinHeight = 580;
        CanResize = true;
        EditorWindowChrome.ConfigureWindow(this);
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://GameplayMachine.Editor/Assets/ODStudio.ico")));
        Content = BuildLayout();

        _tree.ItemTemplate = new FuncDataTemplate<DataTreeItem>((item, _) => BuildTreeRow(item));
        _tree.Classes.Add("compactExplorer");
        _tree.SelectionChanged += (_, _) => SelectTreeItem();
        _tree.DoubleTapped += async (_, _) => await RenameSelectedAsync();
        GameplayObjectReferenceDrag.Attach(_tree, item =>
            _mode == EditorDataMode.Scenes && item is DataTreeItem treeItem && !treeItem.IsRoot
                ? FindSceneObjectOption(treeItem.Id)
                : null);
        _search.TextChanged += (_, _) => RefreshTree();
        _addRoot.Click += async (_, _) => await AddRootAsync();
        _session.ProjectChanged += OnProjectChanged;
        Closed += (_, _) => _session.ProjectChanged -= OnProjectChanged;
        KeyDown += OnKeyDown;

        _selectedId = mode == EditorDataMode.Resources
            ? session.Project.EditorState.SelectedResourceEntityId
            : session.Project.EditorState.SelectedSceneEntityId;
        RefreshTree();
    }

    private Control BuildLayout()
    {
        Grid.SetColumn(_addRoot, 1);
        var root = new Grid { RowDefinitions = new RowDefinitions("44,*,22") };
        root.Children.Add(EditorWindowChrome.BuildTitleBar(this, $"{_session.Project.Name} - {Title}"));
        ToolTip.SetTip(_addRoot, _mode == EditorDataMode.Resources ? "Add resource folder" : "Add scene");

        double explorerWidth = _mode == EditorDataMode.Resources
            ? _session.Project.EditorState.ResourceExplorerWidth
            : _session.Project.EditorState.SceneExplorerWidth;
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{explorerWidth},4,*") };
        var explorer = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#252526")),
            BorderBrush = new SolidColorBrush(Color.Parse("#333333")),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("35,36,*"),
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,22"),
                        Margin = new Thickness(10, 0),
                        Children =
                        {
                            new TextBlock
                            {
                                Text = _mode == EditorDataMode.Resources ? "RESOURCE FOLDERS" : "SCENES",
                                FontSize = 11,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = new SolidColorBrush(Color.Parse("#969696")),
                                VerticalAlignment = VerticalAlignment.Center,
                            },
                            _addRoot,
                        },
                    },
                    _search,
                    _tree,
                },
            },
        };
        Grid.SetRow(_search, 1);
        _search.Margin = new Thickness(8, 3, 8, 7);
        Grid.SetRow(_tree, 2);
        body.Children.Add(explorer);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        splitter.DragCompleted += (_, _) =>
        {
            double width = body.ColumnDefinitions[0].ActualWidth;
            if (_mode == EditorDataMode.Resources)
                _session.Project.EditorState.ResourceExplorerWidth = width;
            else
                _session.Project.EditorState.SceneExplorerWidth = width;
        };
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);

        var document = new Grid { RowDefinitions = new RowDefinitions("35,*") };
        document.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#252526")),
            BorderBrush = new SolidColorBrush(Color.Parse("#333333")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 0),
            Child = _documentTitle,
        });
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _editor,
        };
        Grid.SetRow(scroll, 1);
        document.Children.Add(scroll);
        Grid.SetColumn(document, 2);
        body.Children.Add(document);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var statusBar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#007ACC")),
            Padding = new Thickness(8, 0),
            Child = _status,
        };
        Grid.SetRow(statusBar, 2);
        root.Children.Add(statusBar);
        return root;
    }

    private static Button ExplorerIconButton(string geometry, string tip)
    {
        var button = new Button
        {
            Content = new PathIcon
            {
                Data = StreamGeometry.Parse(geometry),
                Width = 12,
                Height = 12,
            },
            Width = 20,
            Height = 20,
            MinWidth = 20,
            MinHeight = 20,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private Control BuildTreeRow(DataTreeItem? item)
    {
        if (item is null)
            return new TextBlock();
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("20,*,22,22"),
            Margin = new Thickness(item.Depth * 12, 0, 0, 0),
            MinHeight = 20,
        };
        row.Children.Add(new TextBlock
        {
            Text = item.IsSceneRoot ? "★" : item.IsRoot ? "◆" : "•",
            FontSize = item.IsRoot ? 9 : 12,
            Foreground = new SolidColorBrush(Color.Parse(item.IsSceneRoot ? "#E2C08D" : item.IsRoot ? "#4EC9B0" : "#75BEFF")),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        label.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontWeight = item.IsRoot ? FontWeight.SemiBold : FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (!string.IsNullOrEmpty(item.Detail))
            label.Children.Add(new TextBlock
            {
                Text = item.Detail,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.Parse("#858585")),
                VerticalAlignment = VerticalAlignment.Center,
            });
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        if (item.IsRoot)
        {
            var add = ExplorerIconButton(
                "M11 5 L13 5 L13 11 L19 11 L19 13 L13 13 L13 19 L11 19 L11 13 L5 13 L5 11 L11 11 Z",
                _mode == EditorDataMode.Resources ? "Add resource item" : "Add gameplay object");
            add.Click += async (_, eventArgs) =>
            {
                eventArgs.Handled = true;
                await AddChildAsync(item.Id);
            };
            Grid.SetColumn(add, 2);
            row.Children.Add(add);
        }
        var remove = ExplorerIconButton(
            "M6 7 L18 7 L17 21 L7 21 Z M9 3 L15 3 L16 5 L20 5 L20 7 L4 7 L4 5 L8 5 Z M9 9 L11 9 L11 18 L9 18 Z M13 9 L15 9 L15 18 L13 18 Z",
            item.IsRoot ? (_mode == EditorDataMode.Resources ? "Delete resource folder" : "Delete scene") : "Delete item");
        remove.Click += async (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            await DeleteAsync(item.Id);
        };
        Grid.SetColumn(remove, 3);
        row.Children.Add(remove);
        return row;
    }

    private GameplayObjectReferenceOption? FindSceneObjectOption(Guid objectId)
    {
        OdSceneObject? sceneObject = _session.Project.Scenes.SelectMany(item => item.Objects)
            .FirstOrDefault(item => item.Id == objectId);
        return sceneObject is null ? null : ConfiguredValueEditor.SceneOption(ConfigurationProject(), sceneObject);
    }

    private void RefreshTree()
    {
        _refreshing = true;
        string filter = _search.Text?.Trim() ?? string.Empty;
        List<DataTreeItem> items = _mode == EditorDataMode.Resources
            ? ResourceTree(_session.Project, filter)
            : SceneTree(_session.Project, filter);
        _tree.ItemsSource = items;
        _tree.SelectedItem = _selectedId is { } id ? items.FirstOrDefault(item => item.Id == id) : null;
        _refreshing = false;
        RefreshEditor();
    }

    private static List<DataTreeItem> ResourceTree(OdProject project, string filter)
    {
        var result = new List<DataTreeItem>();
        foreach (OdDataSet folder in project.DataSets.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            string typeName = project.Definitions.FirstOrDefault(item => item.Id == folder.DefinitionId)?.Name ?? "Missing type";
            bool rootMatches = Matches(folder.Name, filter) || Matches(typeName, filter);
            List<OdDataRecord> children = folder.Records.Where(item => rootMatches || Matches(item.Name, filter))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (!rootMatches && children.Count == 0)
                continue;
            result.Add(new DataTreeItem(folder.Id, folder.Name, typeName, 0, true, false));
            result.AddRange(children.Select(item => new DataTreeItem(item.Id, item.Name, string.Empty, 1, false, false)));
        }
        return result;
    }

    private static List<DataTreeItem> SceneTree(OdProject project, string filter)
    {
        var result = new List<DataTreeItem>();
        foreach (OdScene scene in project.Scenes.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            bool rootMatches = Matches(scene.Name, filter);
            List<OdSceneObject> children = scene.Objects.Where(item => rootMatches || Matches(item.Name, filter) ||
                    Matches(project.Definitions.FirstOrDefault(definition => definition.Id == item.ClassDefinitionId)?.Name, filter))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (!rootMatches && children.Count == 0)
                continue;
            bool isInitial = project.InitialSceneId == scene.Id;
            result.Add(new DataTreeItem(scene.Id, scene.Name,
                (isInitial ? "INITIAL  " : string.Empty) + $"{scene.Objects.Count} objects", 0, true, isInitial));
            result.AddRange(children.Select(item => new DataTreeItem(item.Id, item.Name,
                project.Definitions.FirstOrDefault(definition => definition.Id == item.ClassDefinitionId)?.Name ?? "Missing class",
                1, false, false)));
        }
        return result;
    }

    private void SelectTreeItem()
    {
        if (_refreshing || _tree.SelectedItem is not DataTreeItem item)
            return;
        _selectedId = item.Id;
        if (_mode == EditorDataMode.Resources)
            _session.Project.EditorState.SelectedResourceEntityId = item.Id;
        else
            _session.Project.EditorState.SelectedSceneEntityId = item.Id;
        RefreshEditor();
    }

    private void RefreshEditor()
    {
        OdEntity? selected = _selectedId is { } id ? _session.Project.FindEntity(id) : null;
        _documentTitle.Text = selected?.Name ?? "Nothing selected";
        _editor.Content = selected switch
        {
            OdDataSet folder => BuildResourceFolderEditor(folder),
            OdDataRecord record => BuildResourceItemEditor(record),
            OdScene scene => BuildSceneEditor(scene),
            OdSceneObject sceneObject => BuildSceneObjectEditor(sceneObject),
            _ => EmptyState(),
        };
    }

    private Control BuildResourceFolderEditor(OdDataSet folder)
    {
        OdDefinition? definition = _session.Project.Definitions.FirstOrDefault(item => item.Id == folder.DefinitionId);
        var panel = EditorPanel($"Resource Folder  {folder.Name}");
        panel.Children.Add(ReadOnlyRow("Resource Type", definition?.Name ?? "Missing type"));
        panel.Children.Add(ReadOnlyRow("Items", folder.Records.Count.ToString()));
        panel.Children.Add(DescriptionEditor(folder.Id, folder.Description));
        panel.Children.Add(Hint("A folder is a typed resource sheet. Every item in this folder uses the same schema."));
        return panel;
    }

    private Control BuildResourceItemEditor(OdDataRecord record)
    {
        OdDataSet? folder = _session.Project.DataSets.FirstOrDefault(item => item.Records.Any(child => child.Id == record.Id));
        OdFieldContainerDefinition? definition = folder is null ? null :
            _session.Project.Definitions.FirstOrDefault(item => item.Id == folder.DefinitionId) as OdFieldContainerDefinition;
        var panel = EditorPanel($"Resource Item  {record.Name}");
        if (folder is null || definition is null)
        {
            panel.Children.Add(Hint("The resource folder type is missing."));
            return panel;
        }
        AddFields(panel, definition, record.Values, null, record.Id, false);
        return panel;
    }

    private Control BuildSceneEditor(OdScene scene)
    {
        var panel = EditorPanel($"Scene  {scene.Name}");
        panel.Children.Add(ReadOnlyRow("OD", _session.Project.Ods.FirstOrDefault(item => item.Id == scene.OdId)?.Name ?? "Missing OD"));
        panel.Children.Add(ReadOnlyRow("Gameplay Objects", scene.Objects.Count.ToString()));
        var isInitial = new CheckBox
        {
            Content = "Initial Scene",
            IsChecked = _session.Project.InitialSceneId == scene.Id,
            Margin = new Thickness(0, 4, 0, 8),
        };
        isInitial.Click += (_, _) => SetInitialScene(scene.Id, isInitial.IsChecked == true);
        panel.Children.Add(isInitial);
        panel.Children.Add(DescriptionEditor(scene.Id, scene.Description));
        panel.Children.Add(Hint("CreateScene creates every object first, then assigns fields so objects may reference each other."));
        return panel;
    }

    private Control BuildSceneObjectEditor(OdSceneObject sceneObject)
    {
        OdScene? scene = _session.Project.Scenes.FirstOrDefault(item => item.Objects.Any(child => child.Id == sceneObject.Id));
        OdClassDefinition? definition = AllDefinitions().OfType<OdClassDefinition>()
            .FirstOrDefault(item => item.Id == sceneObject.ClassDefinitionId);
        var panel = EditorPanel($"Gameplay Object  {sceneObject.Name}");
        if (scene is null || definition is null)
        {
            panel.Children.Add(Hint("The scene or gameplay class is missing."));
            return panel;
        }
        panel.Children.Add(ReadOnlyRow("Scene", scene.Name));
        panel.Children.Add(ReadOnlyRow("Class", definition.Name));
        panel.Children.Add(DescriptionEditor(sceneObject.Id, sceneObject.Description));
        AddFields(panel, definition, sceneObject.Values, scene, sceneObject.Id, true);
        return panel;
    }

    private void AddFields(StackPanel panel, OdFieldContainerDefinition definition,
        IReadOnlyDictionary<Guid, JsonNode?> values, OdScene? scene, Guid ownerId, bool includeLiveReferences)
    {
        foreach ((OdFieldContainerDefinition source, IReadOnlyList<OdFieldDefinition> fields) in FieldGroups(definition))
        {
            panel.Children.Add(SectionTitle(source == definition ? "Fields" : source.Name));
            foreach (OdFieldDefinition field in fields.Where(item => item.Mode != OdFieldMode.Driven))
            {
                if (!includeLiveReferences && IsLiveClassReference(field.Type))
                {
                    panel.Children.Add(ReadOnlyRow(field.Name, "Live gameplay-object references are configured in scenes"));
                    continue;
                }
                JsonNode? current = values.TryGetValue(field.Id, out JsonNode? configured)
                    ? configured
                    : field.DefaultValue;
                var card = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#252526")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#3C3C3C")),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 8),
                    Margin = new Thickness(0, 0, 0, 6),
                };
                var fieldPanel = new StackPanel { Spacing = 5 };
                fieldPanel.Children.Add(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        new TextBlock { Text = field.Name, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = TypeName(field.Type), Foreground = new SolidColorBrush(Color.Parse("#858585")), FontSize = 10, [Grid.ColumnProperty] = 1 },
                    },
                });
                fieldPanel.Children.Add(ConfiguredValueEditor.Build(ConfigurationProject(), field.Type,
                    current?.DeepClone(), scene, value => SetValue(ownerId, field.Id, value)));
                card.Child = fieldPanel;
                panel.Children.Add(card);
            }
        }
    }

    private IEnumerable<(OdFieldContainerDefinition Source, IReadOnlyList<OdFieldDefinition> Fields)> FieldGroups(
        OdFieldContainerDefinition definition)
    {
        if (definition is OdClassDefinition classDefinition)
        {
            var visited = new HashSet<Guid>();
            foreach (var group in InheritedGroups(classDefinition))
                yield return group;
            IEnumerable<(OdFieldContainerDefinition, IReadOnlyList<OdFieldDefinition>)> InheritedGroups(OdClassDefinition current)
            {
                if (!visited.Add(current.Id))
                    yield break;
                foreach (Guid parentId in current.IsClasses)
                    if (AllDefinitions().FirstOrDefault(item => item.Id == parentId) is OdClassDefinition parent)
                        foreach (var group in InheritedGroups(parent))
                            yield return group;
                yield return (current, current.Fields);
            }
            yield break;
        }
        yield return (definition, definition.Fields);
    }

    private void SetValue(Guid ownerId, Guid fieldId, JsonNode? value)
    {
        _session.Execute(OdChangeKind.Update, ownerId,
            _mode == EditorDataMode.Resources ? nameof(OdDataRecord) : nameof(OdSceneObject),
            "Update configured field value", project =>
            {
                IDictionary<Guid, JsonNode?>? values = project.DataSets.SelectMany(item => item.Records)
                    .FirstOrDefault(item => item.Id == ownerId)?.Values;
                values ??= project.Scenes.SelectMany(item => item.Objects)
                    .FirstOrDefault(item => item.Id == ownerId)?.Values;
                if (values is null)
                    return;
                values[fieldId] = value?.DeepClone();
            });
    }

    private void SetInitialScene(Guid sceneId, bool enabled)
    {
        _session.Execute(OdChangeKind.Update, sceneId, nameof(OdScene),
            enabled ? "Set initial scene" : "Clear initial scene", project =>
            {
                if (enabled)
                    project.InitialSceneId = sceneId;
                else if (project.InitialSceneId == sceneId)
                    project.InitialSceneId = null;
            });
    }

    private async Task AddRootAsync()
    {
        if (_mode == EditorDataMode.Resources)
        {
            string? name = await EditorDialogs.PromptNameAsync(this, "New Resource Folder", "Folder name");
            if (string.IsNullOrWhiteSpace(name))
                return;
            name = name.Trim();
            if (_session.Project.DataSets.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                _status.Text = "A resource folder with that name already exists.";
                return;
            }
            TypeChoice? choice = await PickDefinitionTypeAsync(
                "Select Resource Type",
                _session.Project.Definitions.Where(item => item is OdClassDefinition or OdResourceDefinition));
            if (choice?.DefinitionId is not { } definitionId)
                return;
            var folder = new OdDataSet { Name = name, DefinitionId = definitionId };
            _selectedId = folder.Id;
            _session.Execute(OdChangeKind.Create, folder.Id, nameof(OdDataSet), $"Create resource folder {folder.Name}",
                project => project.DataSets.Add(folder));
        }
        else
        {
            List<DefinitionChoice> choices = _session.Project.Ods
                .Select(item => new DefinitionChoice(item.Id, item.Name, item.Namespace)).ToList();
            CreateChoiceResult? result = await EditorDialogs.PromptNameAndChoiceAsync(this,
                "New Scene", "Scene name", choices, "OD");
            if (result is null || result.Choice is not DefinitionChoice choice)
                return;
            if (_session.Project.Scenes.Any(item => item.Name.Equals(result.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _status.Text = "A scene with that name already exists.";
                return;
            }
            var scene = new OdScene { Name = result.Name, OdId = choice.Id };
            _selectedId = scene.Id;
            _session.Execute(OdChangeKind.Create, scene.Id, nameof(OdScene), $"Create scene {scene.Name}",
                project =>
                {
                    project.Scenes.Add(scene);
                    project.InitialSceneId ??= scene.Id;
                });
        }
    }

    private async Task AddChildAsync(Guid ownerId)
    {
        if (_mode == EditorDataMode.Resources)
        {
            OdDataSet? folder = _session.Project.DataSets.FirstOrDefault(item => item.Id == ownerId);
            if (folder is null)
                return;
            string? name = await EditorDialogs.PromptNameAsync(this, "New Resource Item", "Item name");
            if (string.IsNullOrWhiteSpace(name))
                return;
            if (folder.Records.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                _status.Text = "An item with that name already exists in this folder.";
                return;
            }
            var record = new OdDataRecord { Name = name.Trim() };
            _selectedId = record.Id;
            _session.Execute(OdChangeKind.Create, record.Id, nameof(OdDataRecord), $"Create resource item {record.Name}",
                project => project.DataSets.Single(item => item.Id == folder.Id).Records.Add(record));
        }
        else
        {
            OdScene? scene = _session.Project.Scenes.FirstOrDefault(item => item.Id == ownerId);
            if (scene is null)
                return;
            string? name = await EditorDialogs.PromptNameAsync(this, "New Gameplay Object", "Object name");
            if (string.IsNullOrWhiteSpace(name))
                return;
            name = name.Trim();
            if (scene.Objects.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                _status.Text = "An object with that name already exists in this scene.";
                return;
            }
            TypeChoice? choice = await PickDefinitionTypeAsync(
                "Select Gameplay Class",
                _session.Project.Definitions.OfType<OdClassDefinition>()
                    .Where(item => _session.Project.FindOd(item)?.Id == scene.OdId && item.Id != _session.Project.RootClassId));
            if (choice?.DefinitionId is not { } definitionId)
                return;
            var sceneObject = new OdSceneObject { Name = name, ClassDefinitionId = definitionId };
            _selectedId = sceneObject.Id;
            _session.Execute(OdChangeKind.Create, sceneObject.Id, nameof(OdSceneObject), $"Create scene object {sceneObject.Name}",
                project =>
                {
                    OdScene targetScene = project.Scenes.Single(item => item.Id == scene.Id);
                    targetScene.Objects.Add(sceneObject);
                });
        }
    }

    private async Task RenameSelectedAsync()
    {
        if (_selectedId is not { } id || _session.Project.FindEntity(id) is not OdEntity entity)
            return;
        string? name = await EditorDialogs.PromptNameAsync(this, $"Rename {entity.Name}", "Name", entity.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == entity.Name)
            return;
        string newName = name.Trim();
        _session.Execute(OdChangeKind.Rename, id, entity.GetType().Name, $"Rename {entity.Name} to {newName}",
            project => project.FindEntity(id)!.Name = newName);
    }

    private async Task DeleteSelectedAsync()
    {
        if (_selectedId is { } id)
            await DeleteAsync(id);
    }

    private async Task DeleteAsync(Guid id)
    {
        if (_session.Project.FindEntity(id) is not OdEntity entity ||
            !await EditorDialogs.ConfirmAsync(this, "Delete", $"Delete '{entity.Name}'?"))
            return;
        if (_selectedId == id)
            _selectedId = null;
        _session.Execute(OdChangeKind.Delete, id, entity.GetType().Name, $"Delete {entity.Name}", project =>
        {
            project.DataSets.RemoveAll(item => item.Id == id);
            foreach (OdDataSet folder in project.DataSets)
                folder.Records.RemoveAll(item => item.Id == id);
            if (project.InitialSceneId == id)
                project.InitialSceneId = null;
            project.Scenes.RemoveAll(item => item.Id == id);
            foreach (OdScene scene in project.Scenes)
                scene.Objects.RemoveAll(item => item.Id == id);
        });
    }

    private async Task<TypeChoice?> PickDefinitionTypeAsync(string title, IEnumerable<OdDefinition> definitions)
    {
        List<TypeChoice> choices = definitions
            .Select(definition =>
            {
                string category = _session.Project.FindOd(definition)?.Name ?? "Unassigned OD";
                string detail = definition switch
                {
                    OdClassDefinition => "Class",
                    OdResourceDefinition => "Resource",
                    _ => definition.Kind.ToString(),
                };
                return new TypeChoice(OdBuiltInType.None, definition.Id, definition.Name, category, detail);
            })
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (choices.Count == 0)
        {
            _status.Text = "No compatible types are available.";
            return null;
        }
        return await new TypePickerDialog(title, choices, null).ShowDialog<TypeChoice?>(this);
    }

    private Control DescriptionEditor(Guid entityId, string description)
    {
        var editor = new TextBox { Text = description, AcceptsReturn = true, MinHeight = 64, TextWrapping = TextWrapping.Wrap };
        string original = description;
        editor.LostFocus += (_, _) =>
        {
            string value = editor.Text ?? string.Empty;
            if (value == original)
                return;
            original = value;
            _session.Execute(OdChangeKind.Update, entityId, "Entity", "Update description",
                project => project.FindEntity(entityId)!.Description = value);
        };
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = "Description", Foreground = new SolidColorBrush(Color.Parse("#969696")) });
        panel.Children.Add(editor);
        return panel;
    }

    private void OnProjectChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshTree);

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.F2)
        {
            _ = RenameSelectedAsync();
            args.Handled = true;
        }
        else if (args.Key == Key.Delete)
        {
            _ = DeleteSelectedAsync();
            args.Handled = true;
        }
        else if (args.Key == Key.S && args.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = _save();
            args.Handled = true;
        }
    }

    private static StackPanel EditorPanel(string title)
    {
        var panel = new StackPanel { Margin = new Thickness(22, 18, 28, 28), Spacing = 8, MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        return panel;
    }

    private static Control ReadOnlyRow(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), Margin = new Thickness(0, 2) };
        grid.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.Parse("#969696")) });
        var text = new TextBlock { Text = value, [Grid.ColumnProperty] = 1 };
        grid.Children.Add(text);
        return grid;
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 14, 0, 2),
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.Parse("#969696")),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8),
    };

    private static Control EmptyState() => new Grid
    {
        Children =
        {
            new TextBlock
            {
                Text = "Select an item from the left.",
                Foreground = new SolidColorBrush(Color.Parse("#858585")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        },
    };

    private static bool Matches(string? value, string filter) =>
        filter.Length == 0 || value?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;

    private bool IsLiveClassReference(OdTypeReference type) => type.DefinitionId is { } id && !type.IsResourceClass &&
        AllDefinitions().Any(item => item.Id == id && item is OdClassDefinition);

    private string TypeName(OdTypeReference type)
    {
        string element = type.DefinitionId is { } id
            ? AllDefinitions().FirstOrDefault(item => item.Id == id)?.Name ?? "Missing"
            : type.BuiltIn.ToString();
        if (type.IsResourceClass)
            element += " Resource";
        return type.Container == OdContainerKind.Single ? element : $"{type.Container}<{element}>";
    }

    private IEnumerable<OdDefinition> AllDefinitions() =>
        _session.Project.Definitions.Concat(_referencedDefinitions())
            .Concat(OdNetworkPackage.CreateProject(_session.Project.Runtime.NetworkMode).Definitions)
            .GroupBy(item => item.Id)
            .Select(group => group.First());

    private OdProject ConfigurationProject() => new()
    {
        Id = _session.Project.Id,
        Name = _session.Project.Name,
        DefaultNamespace = _session.Project.DefaultNamespace,
        Definitions = AllDefinitions().ToList(),
        DataSets = _session.Project.DataSets,
        Scenes = _session.Project.Scenes,
    };

    private sealed record DataTreeItem(Guid Id, string Name, string Detail, int Depth, bool IsRoot, bool IsSceneRoot);
}

internal sealed class ResourcesEditorWindow : ProjectDataEditorWindow
{
    public ResourcesEditorWindow(OdProjectSession session, Func<Task> save,
        Func<IEnumerable<OdDefinition>> referencedDefinitions)
        : base(session, save, referencedDefinitions, EditorDataMode.Resources) { }
}

internal sealed class SceneEditorWindow : ProjectDataEditorWindow
{
    public SceneEditorWindow(OdProjectSession session, Func<Task> save,
        Func<IEnumerable<OdDefinition>> referencedDefinitions)
        : base(session, save, referencedDefinitions, EditorDataMode.Scenes) { }
}

internal enum EditorDataMode
{
    Resources,
    Scenes,
}

internal sealed record DefinitionChoice(Guid Id, string Name, string Detail)
{
    public override string ToString() => string.IsNullOrEmpty(Detail) ? Name : $"{Name}  ({Detail})";
}

internal sealed record CreateChoiceResult(string Name, object Choice);
