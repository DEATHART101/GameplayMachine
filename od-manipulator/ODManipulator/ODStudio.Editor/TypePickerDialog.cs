using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ODStudio.Model;

namespace ODStudio.Editor;

internal sealed class TypePickerDialog : Window
{
    private const string AllCategory = "All";

    private readonly IReadOnlyList<TypeChoice> _choices;
    private readonly ObservableCollection<TypeChoice> _visibleChoices = new();
    private readonly TextBox _searchBox;
    private readonly ListBox _categoryList;
    private readonly ListBox _typeList;
    private readonly Button _selectButton;
    private readonly Guid? _selectedResourceDefinitionId;

    public TypePickerDialog(string title, IEnumerable<TypeChoice> choices, TypeChoice? selected)
    {
        Title = title;
        Width = 760;
        Height = 540;
        MinWidth = 620;
        MinHeight = 420;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _selectedResourceDefinitionId = selected?.IsResourceClass == true ? selected.DefinitionId : null;

        _choices = choices
            .GroupBy(choice => (choice.BuiltIn, choice.DefinitionId))
            .Select(group => group.First())
            .ToList();
        _searchBox = new TextBox
        {
            PlaceholderText = "Search types",
            Margin = new Thickness(14, 14, 14, 10),
        };
        _categoryList = new ListBox
        {
            ItemsSource = new[] { AllCategory }
                .Concat(_choices.Select(choice => choice.Category).Distinct(StringComparer.Ordinal))
                .ToList(),
            SelectedIndex = 0,
            Margin = new Thickness(8),
        };
        _typeList = new ListBox
        {
            ItemsSource = _visibleChoices,
            ItemTemplate = new FuncDataTemplate<TypeChoice>((choice, _) => BuildTypeRow(choice), true),
            Margin = new Thickness(8),
        };
        _selectButton = new Button
        {
            Content = "Select",
            IsEnabled = false,
            MinWidth = 90,
        };

        Content = BuildContent();
        _searchBox.TextChanged += (_, _) => RefreshChoices();
        _categoryList.SelectionChanged += (_, _) => RefreshChoices();
        _typeList.SelectionChanged += (_, _) =>
            _selectButton.IsEnabled = _typeList.SelectedItem is TypeChoice { CanSelectDirect: true };
        _typeList.DoubleTapped += (_, _) => AcceptSelection();
        _selectButton.Click += (_, _) => AcceptSelection();
        Opened += (_, _) =>
        {
            RefreshChoices();
            TypeChoice? match = selected is null
                ? null
                : _visibleChoices.FirstOrDefault(choice =>
                    choice.BuiltIn == selected.BuiltIn && choice.DefinitionId == selected.DefinitionId);
            if (match is not null)
                _typeList.SelectedItem = match;
            _searchBox.Focus();
        };
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key == Key.Enter && _typeList.SelectedItem is TypeChoice { CanSelectDirect: true })
            {
                AcceptSelection();
                eventArgs.Handled = true;
            }
            else if (eventArgs.Key == Key.Escape)
            {
                Close(null);
                eventArgs.Handled = true;
            }
        };
    }

    private Control BuildContent()
    {
        var cancelButton = new Button { Content = "Cancel", MinWidth = 90 };
        cancelButton.Click += (_, _) => Close(null);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(14, 8, 14, 14),
            Children = { cancelButton, _selectButton },
        };
        var categoryPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                new TextBlock
                {
                    Text = "Packages",
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(10, 8, 10, 0),
                },
                _categoryList,
            },
        };
        Grid.SetRow(_categoryList, 1);

        var typePanel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                new TextBlock
                {
                    Text = "Types",
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(10, 8, 10, 0),
                },
                _typeList,
            },
        };
        Grid.SetRow(_typeList, 1);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("210,5,*"),
            Children =
            {
                categoryPanel,
                new GridSplitter
                {
                    [Grid.ColumnProperty] = 1,
                    ResizeDirection = GridResizeDirection.Columns,
                    Background = new SolidColorBrush(Color.Parse("#2A2D2E")),
                },
                typePanel,
            },
        };
        Grid.SetColumn(typePanel, 2);

        var result = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                _searchBox,
                body,
                buttons,
            },
        };
        Grid.SetRow(body, 1);
        Grid.SetRow(buttons, 2);
        return result;
    }

    private void RefreshChoices()
    {
        string category = _categoryList.SelectedItem as string ?? AllCategory;
        string query = _searchBox.Text?.Trim() ?? string.Empty;
        var matches = _choices
            .Where(choice => category == AllCategory || string.Equals(choice.Category, category, StringComparison.Ordinal))
            .Select(choice => (Choice: choice, Score: OdTypePresentation.FuzzyScore(
                choice.Name, choice.Detail, choice.Category, query)))
            .Where(item => item.Score >= 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Choice.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Choice)
            .ToList();

        _visibleChoices.Clear();
        foreach (TypeChoice choice in matches)
            _visibleChoices.Add(choice);
        if (_typeList.SelectedItem is not TypeChoice selected || !_visibleChoices.Contains(selected))
            _typeList.SelectedItem = null;
    }

    private void AcceptSelection()
    {
        if (_typeList.SelectedItem is TypeChoice { CanSelectDirect: true } selected)
            Close(selected with { IsResourceClass = false });
    }

    private Control BuildTypeRow(TypeChoice choice)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,Auto") };
        row.Children.Add(new TextBlock
        {
            Text = TypeLogo(choice),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#4EC9B0")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var name = new TextBlock
        {
            Text = choice.Name,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            Foreground = choice.CanSelectDirect ? Brushes.White : Brushes.Gray,
        };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        if (choice.CanSelectResourceClass)
        {
            bool selected = _selectedResourceDefinitionId == choice.DefinitionId;
            var resource = new Button
            {
                Content = selected ? "Resource ✓" : "Resource",
                MinWidth = 88,
                Padding = new Thickness(10, 3),
                Margin = new Thickness(8, 2, 2, 2),
            };
            resource.Click += (_, eventArgs) =>
            {
                eventArgs.Handled = true;
                Close(choice with { IsResourceClass = true });
            };
            Grid.SetColumn(resource, 2);
            row.Children.Add(resource);
        }
        return row;
    }

    private static string TypeLogo(TypeChoice choice)
    {
        if (choice.BuiltIn != OdBuiltInType.None)
            return "B";
        return choice.Detail switch
        {
            "Class" => "C",
            "Struct" => "S",
            "Resource" => "R",
            "Switch Struct" => "W",
            "Enum" => "E",
            "Interface" => "I",
            "Event" => "V",
            _ => "?",
        };
    }
}
