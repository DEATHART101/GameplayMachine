using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ODStudio.Model;

namespace ODStudio.Editor;

internal sealed record ResourceReferenceOption(string Id, string Name, string Folder, string TypeName)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Folder) ? Name : $"{Folder} / {Name}";
}

internal static class ResourceReferencePicker
{
    public static Control Build(IReadOnlyList<ResourceReferenceOption> options, string? selectedId,
        Action<string?> changed)
    {
        ResourceReferenceOption? selected = options.FirstOrDefault(item =>
            string.Equals(item.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        var button = new Button
        {
            Content = selected?.DisplayName ?? "(None)",
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        button.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(button) is not Window owner)
                return;
            ResourceReferenceOption? result = await new ResourcePickerDialog(options, selected)
                .ShowDialog<ResourceReferenceOption?>(owner);
            if (result is null)
                return;
            selected = string.IsNullOrEmpty(result.Id) ? null : result;
            button.Content = selected?.DisplayName ?? "(None)";
            changed(selected?.Id);
        };
        return button;
    }
}

internal sealed class ResourcePickerDialog : Window
{
    private const string AllCategory = "All";
    private readonly IReadOnlyList<ResourceReferenceOption> _options;
    private readonly ObservableCollection<ResourceReferenceOption> _visible = new();
    private readonly TextBox _search = new() { PlaceholderText = "Search resources", Margin = new Thickness(14, 14, 14, 10) };
    private readonly ListBox _folders;
    private readonly ListBox _resources;
    private readonly Button _select = new() { Content = "Select", IsEnabled = false, MinWidth = 90 };

    public ResourcePickerDialog(IEnumerable<ResourceReferenceOption> options, ResourceReferenceOption? selected)
    {
        Title = "Select Resource";
        Width = 760;
        Height = 540;
        MinWidth = 620;
        MinHeight = 420;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _options = new[] { new ResourceReferenceOption(string.Empty, "(None)", string.Empty, "Resource") }
            .Concat(options.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
            .ToList();
        _folders = new ListBox
        {
            ItemsSource = new[] { AllCategory }.Concat(_options.Where(item => item.Id.Length > 0)
                .Select(item => item.Folder).Distinct(StringComparer.OrdinalIgnoreCase)).ToList(),
            SelectedIndex = 0,
            Margin = new Thickness(8),
        };
        _resources = new ListBox
        {
            ItemsSource = _visible,
            ItemTemplate = new FuncDataTemplate<ResourceReferenceOption>((item, _) => BuildRow(item), true),
            Margin = new Thickness(8),
        };
        Content = BuildContent();
        _search.TextChanged += (_, _) => Refresh();
        _folders.SelectionChanged += (_, _) => Refresh();
        _resources.SelectionChanged += (_, _) => _select.IsEnabled = _resources.SelectedItem is ResourceReferenceOption;
        _resources.DoubleTapped += (_, _) => Accept();
        _select.Click += (_, _) => Accept();
        Opened += (_, _) =>
        {
            Refresh();
            _resources.SelectedItem = selected is null
                ? _visible.FirstOrDefault(item => item.Id.Length == 0)
                : _visible.FirstOrDefault(item => string.Equals(item.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
            _search.Focus();
        };
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && _resources.SelectedItem is ResourceReferenceOption)
            {
                Accept();
                args.Handled = true;
            }
            else if (args.Key == Key.Escape)
            {
                Close(null);
                args.Handled = true;
            }
        };
    }

    private Control BuildContent()
    {
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        cancel.Click += (_, _) => Close(null);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(14, 8, 14, 14),
            Children = { cancel, _select },
        };
        var folderPanel = Panel("Folders", _folders);
        var resourcePanel = Panel("Resources", _resources);
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("210,5,*"),
            Children =
            {
                folderPanel,
                new GridSplitter
                {
                    [Grid.ColumnProperty] = 1,
                    ResizeDirection = GridResizeDirection.Columns,
                    Background = new SolidColorBrush(Color.Parse("#2A2D2E")),
                },
                resourcePanel,
            },
        };
        Grid.SetColumn(resourcePanel, 2);
        var result = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children = { _search, body, buttons },
        };
        Grid.SetRow(body, 1);
        Grid.SetRow(buttons, 2);
        return result;
    }

    private static Grid Panel(string title, Control content)
    {
        var result = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                new TextBlock { Text = title, Foreground = Brushes.Gray, Margin = new Thickness(10, 8, 10, 0) },
                content,
            },
        };
        Grid.SetRow(content, 1);
        return result;
    }

    private void Refresh()
    {
        string folder = _folders.SelectedItem as string ?? AllCategory;
        string query = _search.Text?.Trim() ?? string.Empty;
        IEnumerable<ResourceReferenceOption> matches = _options
            .Where(item => folder == AllCategory || string.Equals(item.Folder, folder, StringComparison.OrdinalIgnoreCase))
            .Select(item => (Item: item, Score: OdTypePresentation.FuzzyScore(
                item.Name, item.TypeName, item.Folder, query)))
            .Where(item => item.Score >= 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Item);
        _visible.Clear();
        foreach (ResourceReferenceOption item in matches)
            _visible.Add(item);
    }

    private void Accept()
    {
        if (_resources.SelectedItem is ResourceReferenceOption selected)
            Close(selected);
    }

    private static Control BuildRow(ResourceReferenceOption item)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,Auto") };
        row.Children.Add(new TextBlock
        {
            Text = "R",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#4EC9B0")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var name = new TextBlock { Text = item.DisplayName, Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        var type = new TextBlock { Text = item.TypeName, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(type, 2);
        row.Children.Add(type);
        return row;
    }
}
