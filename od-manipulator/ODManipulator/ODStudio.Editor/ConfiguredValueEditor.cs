using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ODStudio.Model;

namespace ODStudio.Editor;

internal static class ConfiguredValueEditor
{
    public static Control Build(OdProject project, OdTypeReference type, JsonNode? value,
        OdScene? scene, Action<JsonNode?> changed) =>
        type.Container switch
        {
            OdContainerKind.List or OdContainerKind.Set => BuildSequence(project, type, value as JsonArray, scene, changed),
            OdContainerKind.Dictionary => BuildDictionary(project, type, value as JsonObject, scene, changed),
            _ => BuildSingle(project, type, value, scene, changed),
        };

    private static Control BuildSingle(OdProject project, OdTypeReference type, JsonNode? value,
        OdScene? scene, Action<JsonNode?> changed)
    {
        if (type.DefinitionId is { } definitionId &&
            project.Definitions.FirstOrDefault(item => item.Id == definitionId) is { } definition)
        {
            if (definition is OdEnumDefinition enumDefinition)
                return BuildEnum(enumDefinition, value, changed);
            if (definition is OdInterfaceDefinition interfaceDefinition)
                return BuildInterfaceCall(project, interfaceDefinition, value as JsonObject, scene, changed);
            if (definition is OdClassDefinition && !type.IsResourceClass)
                return BuildSceneReference(project, definitionId, value, scene, changed);
            if (definition is OdClassDefinition or OdResourceDefinition)
                return BuildResourceReference(project, definitionId, value, changed);
            if (definition is OdSwitchStructDefinition switchDefinition)
                return BuildSwitchStructure(project, switchDefinition, value as JsonObject, scene, changed);
            if (definition is OdFieldContainerDefinition container)
                return BuildStructure(project, container, value as JsonObject, scene, changed);
        }

        if (type.BuiltIn == OdBuiltInType.Boolean)
        {
            var check = new CheckBox { IsChecked = TryGet(value, false) };
            check.IsCheckedChanged += (_, _) => changed(JsonValue.Create(check.IsChecked == true));
            return check;
        }

        var editor = new TextBox { Text = DisplayScalar(type.BuiltIn, value), HorizontalAlignment = HorizontalAlignment.Stretch };
        string original = editor.Text ?? string.Empty;
        void Commit()
        {
            string text = editor.Text ?? string.Empty;
            if (text == original)
                return;
            if (!TryParseScalar(type.BuiltIn, text, out JsonNode? parsed))
            {
                editor.Text = original;
                return;
            }
            original = text;
            changed(parsed);
        }
        editor.LostFocus += (_, _) => Commit();
        editor.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter)
                return;
            Commit();
            args.Handled = true;
        };
        return editor;
    }

    private static Control BuildEnum(OdEnumDefinition definition, JsonNode? value, Action<JsonNode?> changed)
    {
        var choices = definition.Members.OrderBy(item => item.Value)
            .Select(item => new EnumChoice(item.Value, item.Name))
            .ToList();
        var picker = new ComboBox { ItemsSource = choices, HorizontalAlignment = HorizontalAlignment.Stretch };
        int current = TryGet(value, choices.FirstOrDefault()?.Value ?? 0);
        picker.SelectedItem = choices.FirstOrDefault(item => item.Value == current) ?? choices.FirstOrDefault();
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is EnumChoice member)
                changed(JsonValue.Create(member.Value));
        };
        return picker;
    }

    private static Control BuildInterfaceCall(OdProject project, OdInterfaceDefinition declaredInterface,
        JsonObject? value, OdScene? scene, Action<JsonNode?> changed)
    {
        List<InterfaceCallChoice> choices = project.Definitions.OfType<OdInterfaceDefinition>()
            .Where(candidate => !candidate.Abstract && candidate.InterfaceType == OdInterfaceType.Gameplay &&
                                !candidate.IsRoutine && InterfaceHierarchy(project, candidate).All(item => item.Outputs.Count == 0) &&
                                IsInterfaceAssignableTo(project, candidate, declaredInterface))
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new InterfaceCallChoice(candidate))
            .ToList();
        if (choices.Count == 0)
            return new TextBlock
            {
                Text = $"No concrete interfaces implement {declaredInterface.Name}.",
                Foreground = Brushes.Gray,
            };

        JsonObject current = value?.DeepClone() as JsonObject ??
                             OdInterfaceCallValue.Create(choices[0].Definition.Id);
        InterfaceCallChoice selected = OdInterfaceCallValue.TryGetInterfaceId(current, out Guid selectedId)
            ? choices.FirstOrDefault(choice => choice.Definition.Id == selectedId) ?? choices[0]
            : choices[0];
        if (!OdInterfaceCallValue.TryGetInterfaceId(current, out _) || selected.Definition.Id != selectedId)
            current = OdInterfaceCallValue.Create(selected.Definition.Id);

        var picker = new ComboBox
        {
            ItemsSource = choices,
            SelectedItem = selected,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var editorHost = new ContentControl();

        void Render(InterfaceCallChoice choice)
        {
            JsonObject? existingValues = OdInterfaceCallValue.GetValues(current);
            JsonObject values;
            if (existingValues is null)
            {
                values = new JsonObject();
                current[OdInterfaceCallValue.ValuesProperty] = values;
            }
            else
            {
                values = existingValues;
            }
            var panel = new StackPanel { Spacing = 6 };
            foreach (OdFieldDefinition field in InterfaceHierarchy(project, choice.Definition).SelectMany(item => item.Inputs))
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 8 };
                row.Children.Add(new TextBlock
                {
                    Text = field.Name,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                string key = field.Id.ToString("D");
                bool runtimeOnly = scene is null && field.Type.Container == OdContainerKind.Single &&
                                   field.Type.DefinitionId is { } fieldTypeId && !field.Type.IsResourceClass &&
                                   project.Definitions.FirstOrDefault(item => item.Id == fieldTypeId) is OdClassDefinition;
                Control editor;
                if (runtimeOnly)
                {
                    editor = new TextBlock
                    {
                        Text = "Runtime value",
                        Foreground = Brushes.Gray,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                }
                else
                {
                    JsonNode? configured = values[key] ?? values[field.Name] ?? field.DefaultValue ??
                                           DefaultNode(project, field.Type);
                    editor = Build(project, field.Type, configured?.DeepClone(), scene, newValue =>
                    {
                        values[key] = newValue?.DeepClone();
                        changed(current.DeepClone());
                    });
                }
                Grid.SetColumn(editor, 1);
                row.Children.Add(editor);
                panel.Children.Add(row);
            }
            editorHost.Content = panel;
        }

        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is not InterfaceCallChoice choice || choice.Definition.Id == selected.Definition.Id)
                return;
            selected = choice;
            current = OdInterfaceCallValue.Create(choice.Definition.Id);
            Render(choice);
            changed(current.DeepClone());
        };
        Render(selected);

        var typeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 8 };
        typeRow.Children.Add(new TextBlock { Text = "Concrete Interface", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(picker, 1);
        typeRow.Children.Add(picker);
        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#3C3C3C")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = new StackPanel { Spacing = 8, Children = { typeRow, editorHost } },
        };
    }

    private static IReadOnlyList<OdInterfaceDefinition> InterfaceHierarchy(OdProject project,
        OdInterfaceDefinition definition)
    {
        var result = new List<OdInterfaceDefinition>();
        var visited = new HashSet<Guid>();
        void Add(OdInterfaceDefinition current)
        {
            if (!visited.Add(current.Id))
                return;
            if (current.BaseInterfaceId is { } baseId &&
                project.Definitions.FirstOrDefault(item => item.Id == baseId) is OdInterfaceDefinition baseInterface)
                Add(baseInterface);
            result.Add(current);
        }
        Add(definition);
        return result;
    }

    private static bool IsInterfaceAssignableTo(OdProject project, OdInterfaceDefinition actual,
        OdInterfaceDefinition expected) =>
        InterfaceHierarchy(project, actual).Any(item => item.Id == expected.Id);

    private static Control BuildResourceReference(OdProject project, Guid definitionId, JsonNode? value,
        Action<JsonNode?> changed)
    {
        string typeName = project.Definitions.FirstOrDefault(item => item.Id == definitionId)?.Name ?? "Resource";
        List<ResourceReferenceOption> choices = project.DataSets.Where(folder => folder.DefinitionId == definitionId)
            .SelectMany(folder => folder.Records.Select(record => new ResourceReferenceOption(
                record.Id.ToString("N"), record.Name, folder.Name, typeName)))
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return ResourceReferencePicker.Build(choices, TryGuid(value).ToString("N"), selected =>
            changed(string.IsNullOrEmpty(selected) ? null : JsonValue.Create(Guid.Parse(selected).ToString("D"))));
    }

    private static Control BuildSceneReference(OdProject project, Guid definitionId, JsonNode? value,
        OdScene? scene, Action<JsonNode?> changed)
    {
        List<GameplayObjectReferenceOption> choices = scene is null
            ? new List<GameplayObjectReferenceOption>()
            : scene.Objects.Select(item => SceneOption(project, item))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        string requiredName = project.Definitions.FirstOrDefault(item => item.Id == definitionId)?.Name ?? "GameplayObject";
        string requiredType = definitionId.ToString("N");
        return new GameplayObjectReferenceEditor(choices, TryGuid(value).ToString("N"), requiredType,
            requiredName, selected => changed(selected is null ? null : JsonValue.Create(Guid.Parse(selected.Key).ToString("D"))));
    }

    private static Control BuildStructure(OdProject project, OdFieldContainerDefinition definition,
        JsonObject? value, OdScene? scene, Action<JsonNode?> changed)
    {
        JsonObject current = value?.DeepClone() as JsonObject ?? new JsonObject();
        var panel = new StackPanel { Spacing = 5 };
        foreach (OdFieldDefinition field in definition.Fields)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 8 };
            row.Children.Add(new TextBlock { Text = field.Name, VerticalAlignment = VerticalAlignment.Center });
            Control child = Build(project, field.Type, Child(current, field), scene, newValue =>
            {
                current[field.Id.ToString("D")] = newValue?.DeepClone();
                changed(current.DeepClone());
            });
            Grid.SetColumn(child, 1);
            row.Children.Add(child);
            panel.Children.Add(row);
        }
        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#3C3C3C")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = panel,
        };
    }

    private static Control BuildSwitchStructure(OdProject project, OdSwitchStructDefinition definition,
        JsonObject? value, OdScene? scene, Action<JsonNode?> changed)
    {
        JsonObject current = value?.DeepClone() as JsonObject ?? new JsonObject();
        List<SwitchVariantChoice> choices = definition.Fields
            .Select(field => new SwitchVariantChoice(field))
            .ToList();
        if (choices.Count == 0)
            return new TextBlock { Text = "This switch struct has no variants.", Foreground = Brushes.Gray };

        SwitchVariantChoice selected = choices.FirstOrDefault(choice => HasChild(current, choice.Field)) ?? choices[0];
        var picker = new ComboBox
        {
            ItemsSource = choices,
            SelectedItem = selected,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var editorHost = new ContentControl();

        void Render(SwitchVariantChoice choice)
        {
            OdFieldDefinition field = choice.Field;
            JsonNode? childValue = Child(current, field) ?? DefaultNode(project, field.Type);
            var content = new StackPanel { Spacing = 5 };
            content.Children.Add(new TextBlock
            {
                Text = field.Name,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            });
            content.Children.Add(Build(project, field.Type, childValue, scene, newValue =>
            {
                current.Clear();
                current[field.Id.ToString("D")] = newValue?.DeepClone();
                changed(current.DeepClone());
            }));
            editorHost.Content = content;
        }

        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is not SwitchVariantChoice choice || choice == selected)
                return;
            selected = choice;
            current.Clear();
            current[choice.Field.Id.ToString("D")] = DefaultNode(project, choice.Field.Type);
            Render(choice);
            changed(current.DeepClone());
        };

        var typeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 8 };
        typeRow.Children.Add(new TextBlock { Text = "Type", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(picker, 1);
        typeRow.Children.Add(picker);
        Render(selected);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#3C3C3C")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = new StackPanel
            {
                Spacing = 8,
                Children = { typeRow, editorHost },
            },
        };
    }

    private static Control BuildSequence(OdProject project, OdTypeReference type, JsonArray? value,
        OdScene? scene, Action<JsonNode?> changed)
    {
        JsonArray current = value?.DeepClone() as JsonArray ?? new JsonArray();
        var panel = new StackPanel { Spacing = 4 };
        var elementType = ElementType(type);
        for (int index = 0; index < current.Count; index++)
        {
            int captured = index;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,28"), ColumnSpacing = 4 };
            row.Children.Add(new TextBlock { Text = index.ToString(CultureInfo.InvariantCulture), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray });
            Control itemEditor = Build(project, elementType, current[index], scene, newValue =>
            {
                current[captured] = newValue?.DeepClone();
                changed(current.DeepClone());
            });
            Grid.SetColumn(itemEditor, 1);
            row.Children.Add(itemEditor);
            var remove = IconButton("-", "Remove item");
            remove.Click += (_, _) =>
            {
                current.RemoveAt(captured);
                changed(current.DeepClone());
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            panel.Children.Add(row);
        }
        var add = new Button { Content = "+ Add Item", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) =>
        {
            current.Add(DefaultNode(project, elementType));
            changed(current.DeepClone());
        };
        panel.Children.Add(add);
        return panel;
    }

    private static Control BuildDictionary(OdProject project, OdTypeReference type, JsonObject? value,
        OdScene? scene, Action<JsonNode?> changed)
    {
        JsonObject current = value?.DeepClone() as JsonObject ?? new JsonObject();
        var panel = new StackPanel { Spacing = 4 };
        var valueType = ElementType(type);
        var keyType = DictionaryKeyType(type);
        foreach ((string key, JsonNode? node) in current.ToList())
        {
            string currentKey = key;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*,28"), ColumnSpacing = 4 };
            Control keyEditor = Build(project, keyType, ParseDictionaryKey(keyType, key), scene, newKeyValue =>
            {
                string newKey = SerializeDictionaryKey(keyType, newKeyValue);
                if (newKey.Length == 0 || newKey == currentKey || current.ContainsKey(newKey))
                    return;
                JsonNode? oldValue = current[currentKey]?.DeepClone();
                current.Remove(currentKey);
                current[newKey] = oldValue;
                currentKey = newKey;
                changed(current.DeepClone());
            });
            row.Children.Add(keyEditor);
            Control valueEditor = Build(project, valueType, node, scene, newValue =>
            {
                current[currentKey] = newValue?.DeepClone();
                changed(current.DeepClone());
            });
            Grid.SetColumn(valueEditor, 1);
            row.Children.Add(valueEditor);
            var remove = IconButton("-", "Remove entry");
            remove.Click += (_, _) =>
            {
                current.Remove(currentKey);
                changed(current.DeepClone());
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            panel.Children.Add(row);
        }
        var add = new Button { Content = "+ Add Entry", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) =>
        {
            string? key = NextKey(project, current, keyType);
            if (key is null)
                return;
            current[key] = DefaultNode(project, valueType);
            changed(current.DeepClone());
        };
        panel.Children.Add(add);
        return panel;
    }

    public static JsonNode? DefaultNode(OdProject project, OdTypeReference type)
    {
        if (type.Container is OdContainerKind.List or OdContainerKind.Set)
            return new JsonArray();
        if (type.Container == OdContainerKind.Dictionary)
            return new JsonObject();
        if (type.DefinitionId is { } definitionId && project.Definitions.FirstOrDefault(item => item.Id == definitionId) is { } definition)
        {
            if (definition is OdEnumDefinition enumDefinition)
                return JsonValue.Create(enumDefinition.Members.FirstOrDefault()?.Value ?? 0);
            if (definition is OdSwitchStructDefinition switchDefinition)
            {
                OdFieldDefinition? first = switchDefinition.Fields.FirstOrDefault();
                return first is null
                    ? new JsonObject()
                    : new JsonObject { [first.Id.ToString("D")] = DefaultNode(project, first.Type) };
            }
            if (definition is OdStructDefinition)
                return new JsonObject();
            return null;
        }
        return type.BuiltIn switch
        {
            OdBuiltInType.Boolean => JsonValue.Create(false),
            OdBuiltInType.Int32 => JsonValue.Create(0),
            OdBuiltInType.Int64 => JsonValue.Create(0L),
            OdBuiltInType.Single => JsonValue.Create(0f),
            OdBuiltInType.Double => JsonValue.Create(0d),
            OdBuiltInType.Decimal => JsonValue.Create(0m),
            OdBuiltInType.DateTime => JsonValue.Create(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            _ => JsonValue.Create(string.Empty),
        };
    }

    private static OdTypeReference ElementType(OdTypeReference type) => new()
    {
        BuiltIn = type.BuiltIn,
        DefinitionId = type.DefinitionId,
        IsResourceClass = type.IsResourceClass,
        Nullable = type.Nullable,
    };

    private static OdTypeReference DictionaryKeyType(OdTypeReference type) => new()
    {
        BuiltIn = type.DictionaryKeyBuiltIn,
        DefinitionId = type.DictionaryKeyDefinitionId,
        IsResourceClass = type.DictionaryKeyIsResourceClass,
    };

    private static JsonNode? ParseDictionaryKey(OdTypeReference type, string key)
    {
        if (type.DefinitionId is not null)
            return JsonValue.Create(key);
        if (type.BuiltIn == OdBuiltInType.Boolean && bool.TryParse(key, out bool boolean))
            return JsonValue.Create(boolean);
        return TryParseScalar(type.BuiltIn, key, out JsonNode? value) ? value : JsonValue.Create(key);
    }

    private static string SerializeDictionaryKey(OdTypeReference type, JsonNode? value)
    {
        if (value is null)
            return string.Empty;
        if (type.DefinitionId is not null || type.BuiltIn is OdBuiltInType.String or OdBuiltInType.DateTime)
            return TryGet(value, string.Empty);
        return value.ToJsonString().Trim('"');
    }

    private static JsonNode? Child(JsonObject source, OdFieldDefinition field) =>
        source[field.Id.ToString("D")] ?? source[field.Name];

    private static bool HasChild(JsonObject source, OdFieldDefinition field) =>
        source.ContainsKey(field.Id.ToString("D")) || source.ContainsKey(field.Name);

    private static string DisplayScalar(OdBuiltInType type, JsonNode? value)
    {
        if (value is null)
            return string.Empty;
        if (type == OdBuiltInType.String || type == OdBuiltInType.DateTime)
            return TryGet(value, string.Empty);
        return value.ToJsonString().Trim('"');
    }

    private static bool TryParseScalar(OdBuiltInType type, string text, out JsonNode? value)
    {
        value = null;
        switch (type)
        {
            case OdBuiltInType.Int32 when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int int32):
                value = JsonValue.Create(int32); return true;
            case OdBuiltInType.Int64 when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long int64):
                value = JsonValue.Create(int64); return true;
            case OdBuiltInType.Single when float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float single):
                value = JsonValue.Create(single); return true;
            case OdBuiltInType.Double when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number):
                value = JsonValue.Create(number); return true;
            case OdBuiltInType.Decimal when decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal decimalValue):
                value = JsonValue.Create(decimalValue); return true;
            case OdBuiltInType.DateTime when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dateTime):
                value = JsonValue.Create(dateTime.ToString("O", CultureInfo.InvariantCulture)); return true;
            case OdBuiltInType.String:
                value = JsonValue.Create(text); return true;
            default:
                return false;
        }
    }

    private static T TryGet<T>(JsonNode? value, T fallback)
    {
        try { return value is null ? fallback : value.GetValue<T>(); }
        catch { return fallback; }
    }

    private static Guid TryGuid(JsonNode? value) => Guid.TryParse(TryGet(value, string.Empty), out Guid id) ? id : Guid.Empty;

    internal static GameplayObjectReferenceOption SceneOption(OdProject project, OdSceneObject item)
    {
        OdClassDefinition? actual = project.Definitions.OfType<OdClassDefinition>()
            .FirstOrDefault(definition => definition.Id == item.ClassDefinitionId);
        var assignable = new HashSet<string>(StringComparer.Ordinal);
        if (actual is not null)
            AddAssignable(actual);
        return new GameplayObjectReferenceOption(item.Id.ToString("N"), item.Name,
            actual?.Name ?? "Missing class", item.ClassDefinitionId.ToString("N"), assignable);

        void AddAssignable(OdClassDefinition current)
        {
            if (!assignable.Add(current.Id.ToString("N")))
                return;
            foreach (Guid parentId in current.IsClasses)
                if (project.Definitions.FirstOrDefault(definition => definition.Id == parentId) is OdClassDefinition parent)
                    AddAssignable(parent);
        }
    }

    private static Button IconButton(string text, string tip)
    {
        var button = new Button { Content = text, Width = 24, Height = 24, Padding = new Thickness(0) };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static string? NextKey(OdProject project, JsonObject map, OdTypeReference keyType)
    {
        if (keyType.DefinitionId is { } definitionId)
        {
            if (project.Definitions.FirstOrDefault(item => item.Id == definitionId) is OdEnumDefinition enumDefinition)
                return enumDefinition.Members.Select(item => item.Value.ToString(CultureInfo.InvariantCulture))
                    .FirstOrDefault(key => !map.ContainsKey(key));
            return project.DataSets.Where(folder => folder.DefinitionId == definitionId)
                .SelectMany(folder => folder.Records)
                .Select(record => record.Id.ToString("D"))
                .FirstOrDefault(key => !map.ContainsKey(key));
        }
        if (keyType.BuiltIn == OdBuiltInType.Boolean)
        {
            if (!map.ContainsKey("false")) return "false";
            return !map.ContainsKey("true") ? "true" : null;
        }
        if (keyType.BuiltIn is OdBuiltInType.Int32 or OdBuiltInType.Int64 or OdBuiltInType.Single or
            OdBuiltInType.Double or OdBuiltInType.Decimal)
        {
            for (int index = 0; ; index++)
            {
                string numericKey = index.ToString(CultureInfo.InvariantCulture);
                if (!map.ContainsKey(numericKey))
                    return numericKey;
            }
        }
        for (int index = 1; ; index++)
        {
            string key = $"Key{index}";
            if (!map.ContainsKey(key))
                return key;
        }
    }

    private sealed record EnumChoice(int Value, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record SwitchVariantChoice(OdFieldDefinition Field)
    {
        public override string ToString() => Field.Name;
    }

    private sealed record InterfaceCallChoice(OdInterfaceDefinition Definition)
    {
        public override string ToString() => Definition.Name;
    }
}
