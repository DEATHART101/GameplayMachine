using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ODStudio.Editor;

internal sealed record GameplayObjectReferenceOption(
    string Key,
    string Name,
    string TypeName,
    string TypeKey,
    IReadOnlySet<string> AssignableTypeKeys)
{
    public override string ToString() => $"{Name}  [{TypeName}]";
}

internal sealed class GameplayObjectReferenceEditor : Border
{
    private static readonly IBrush NormalBorder = new SolidColorBrush(Color.Parse("#3C3C3C"));
    private static readonly IBrush ValidBorder = new SolidColorBrush(Color.Parse("#2EA043"));
    private static readonly IBrush InvalidBorder = new SolidColorBrush(Color.Parse("#F14C4C"));
    private readonly ComboBox _picker;
    private readonly TextBlock _feedback;
    private readonly Popup _feedbackPopup;
    private readonly string _requiredTypeKey;
    private readonly string _requiredTypeName;
    private readonly Action<GameplayObjectReferenceOption?> _changed;

    public GameplayObjectReferenceEditor(
        IReadOnlyList<GameplayObjectReferenceOption> choices,
        string? selectedKey,
        string requiredTypeKey,
        string requiredTypeName,
        Action<GameplayObjectReferenceOption?> changed)
    {
        _requiredTypeKey = requiredTypeKey;
        _requiredTypeName = requiredTypeName;
        _changed = changed;
        var items = new List<GameplayObjectReferenceOption?> { null };
        items.AddRange(choices.Where(IsCompatible));
        _picker = new ComboBox
        {
            ItemsSource = items,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "(None)",
            SelectedItem = items.FirstOrDefault(item => item?.Key == selectedKey),
        };
        _picker.SelectionChanged += (_, _) => _changed(_picker.SelectedItem as GameplayObjectReferenceOption);
        _feedback = new TextBlock
        {
            FontSize = 11,
            Foreground = InvalidBorder,
            TextWrapping = TextWrapping.Wrap,
        };
        _feedbackPopup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 3,
            IsLightDismissEnabled = false,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#252526")),
                BorderBrush = InvalidBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 5),
                MaxWidth = 340,
                IsHitTestVisible = false,
                Child = _feedback,
            },
        };
        Child = new Grid { Children = { _picker, _feedbackPopup } };
        BorderBrush = NormalBorder;
        BorderThickness = new Thickness(1);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public GameplayObjectReferenceOption? Selected => _picker.SelectedItem as GameplayObjectReferenceOption;

    private void OnDragOver(object? sender, DragEventArgs args)
    {
        GameplayObjectReferenceOption? option = GameplayObjectReferenceDrag.Read(args);
        if (option is null)
        {
            args.DragEffects = DragDropEffects.None;
            ShowFeedback(false, "Only GameplayObjects can be assigned here.");
            return;
        }
        bool compatible = IsCompatible(option);
        args.DragEffects = compatible ? DragDropEffects.Copy : DragDropEffects.None;
        ShowFeedback(compatible, compatible
            ? $"Assign {option.Name}"
            : $"Cannot assign {option.TypeName}; this field requires {_requiredTypeName}.");
    }

    private void OnDragLeave(object? sender, RoutedEventArgs args) => ClearFeedback();

    private void OnDrop(object? sender, DragEventArgs args)
    {
        GameplayObjectReferenceOption? option = GameplayObjectReferenceDrag.Read(args);
        if (option is not null && IsCompatible(option))
        {
            _picker.SelectedItem = (_picker.ItemsSource as IEnumerable<GameplayObjectReferenceOption?>)?
                .FirstOrDefault(item => item?.Key == option.Key);
            args.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            args.DragEffects = DragDropEffects.None;
        }
        ClearFeedback();
    }

    private bool IsCompatible(GameplayObjectReferenceOption option) =>
        string.IsNullOrEmpty(_requiredTypeKey) || option.AssignableTypeKeys.Contains(_requiredTypeKey);

    private void ShowFeedback(bool valid, string message)
    {
        BorderBrush = valid ? ValidBorder : InvalidBorder;
        Background = new SolidColorBrush(Color.Parse(valid ? "#142A18" : "#321B1B"));
        _feedbackPopup.IsOpen = !valid;
        if (!valid)
            _feedback.Text = message;
    }

    private void ClearFeedback()
    {
        BorderBrush = NormalBorder;
        Background = Brushes.Transparent;
        _feedbackPopup.IsOpen = false;
    }
}

internal static class GameplayObjectReferenceDrag
{
    private static readonly DataFormat<GameplayObjectReferenceOption> Format =
        DataFormat.CreateInProcessFormat<GameplayObjectReferenceOption>("gameplaymachine-gameplay-object-reference");

    public static void Attach(ListBox control, Func<object?, GameplayObjectReferenceOption?> value)
    {
        PointerPressedEventArgs? pressed = null;
        IPointer? pointer = null;
        GameplayObjectReferenceOption? dragged = null;
        object? pressedItem = null;
        Point start = default;
        control.AddHandler(InputElement.PointerPressedEvent, (_, args) =>
        {
            if (args.GetCurrentPoint(control).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
                return;
            if (IsInteractiveChild(control, args.Source as Visual))
                return;
            ListBoxItem? container = FindItemContainer(args.Source as Visual);
            if (container is null)
                return;
            GameplayObjectReferenceOption? option = value(container.DataContext ?? container.Content);
            if (option is null)
                return;
            pressed = args;
            pointer = args.Pointer;
            dragged = option;
            pressedItem = container.DataContext ?? container.Content;
            start = args.GetPosition(control);
            pointer.Capture(control);
            args.Handled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        control.AddHandler(InputElement.PointerReleasedEvent, (_, args) =>
        {
            object? itemToSelect = pressed is not null ? pressedItem : null;
            if (itemToSelect is not null)
            {
                control.SelectedItem = itemToSelect;
                args.Handled = true;
            }
            if (pointer == args.Pointer)
                pointer.Capture(null);
            pressed = null;
            pointer = null;
            dragged = null;
            pressedItem = null;
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        control.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) =>
        {
            pressed = null;
            pointer = null;
            dragged = null;
            pressedItem = null;
        }, RoutingStrategies.Direct, handledEventsToo: true);
        control.AddHandler(InputElement.PointerMovedEvent, async (_, args) =>
        {
            if (pressed is null || dragged is null ||
                !args.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
                return;
            Point current = args.GetPosition(control);
            if (Math.Abs(current.X - start.X) < 4 && Math.Abs(current.Y - start.Y) < 4)
                return;
            PointerPressedEventArgs trigger = pressed;
            GameplayObjectReferenceOption option = dragged;
            pressed = null;
            dragged = null;
            pressedItem = null;
            pointer?.Capture(null);
            pointer = null;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(Format, option));
            await DragDrop.DoDragDropAsync(trigger, transfer, DragDropEffects.Copy);
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static ListBoxItem? FindItemContainer(Visual? source)
    {
        if (source is ListBoxItem direct)
            return direct;
        return source?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
    }

    private static bool IsInteractiveChild(ListBox dragSource, Visual? source)
    {
        for (Visual? current = source; current is not null && current != dragSource; current = current.GetVisualParent())
            if (current is Button or TextBox or ComboBox)
                return true;
        return false;
    }

    public static GameplayObjectReferenceOption? Read(DragEventArgs args) =>
        args.DataTransfer.TryGetValue(Format);
}
