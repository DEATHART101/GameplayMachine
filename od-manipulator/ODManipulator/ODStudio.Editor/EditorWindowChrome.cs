using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ODStudio.Editor;

internal static class EditorWindowChrome
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const uint DwmCornerPreferenceRound = 2;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const double ResizeBorderThickness = 5;
    private const double ResizeCornerSize = 9;

    private static readonly ConditionalWeakTable<Window, object> ResizeLayers = new();

    private static readonly StreamGeometry MinimizeIcon = StreamGeometry.Parse("M5 11 L19 11 L19 13 L5 13 Z");
    private static readonly StreamGeometry MaximizeIcon = StreamGeometry.Parse(
        "M5 5 L19 5 L19 7 L5 7 Z M5 17 L19 17 L19 19 L5 19 Z M5 7 L7 7 L7 17 L5 17 Z M17 7 L19 7 L19 17 L17 17 Z");
    private static readonly StreamGeometry CloseIcon = StreamGeometry.Parse(
        "M6 4 L12 10 L18 4 L20 6 L14 12 L20 18 L18 20 L12 14 L6 20 L4 18 L10 12 L4 6 Z");

    public static void ConfigureWindow(Window window)
    {
        window.ExtendClientAreaToDecorationsHint = true;
        window.WindowDecorations = WindowDecorations.None;
        window.Opened += (_, _) =>
        {
            InstallResizeLayer(window);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                ApplyWindowsChrome(window);
        };
    }

    public static Control BuildTitleBar(Window window, string title)
    {
        var titleText = new TextBlock
        {
            Text = title,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#BFBFBF")),
        };
        var dragRegion = new Border { Background = Brushes.Transparent, Child = titleText };
        dragRegion.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(window).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
                return;
            if (args.ClickCount == 2)
                window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else
                window.BeginMoveDrag(args);
        };

        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("56,56,56") };
        Button minimize = CaptionButton(MinimizeIcon, "Minimize");
        minimize.Click += (_, _) => window.WindowState = WindowState.Minimized;
        buttons.Children.Add(minimize);
        Button maximize = CaptionButton(MaximizeIcon, "Maximize");
        maximize.Click += (_, _) =>
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        Grid.SetColumn(maximize, 1);
        buttons.Children.Add(maximize);
        Button close = CaptionButton(CloseIcon, "Close");
        close.Classes.Add("close");
        close.Click += (_, _) => window.Close();
        Grid.SetColumn(close, 2);
        buttons.Children.Add(close);

        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*,168") };
        bar.Children.Add(new Image
        {
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://GameplayMachine.Editor/Assets/ODStudio.png"))),
            Width = 20,
            Height = 20,
            Margin = new Thickness(8, 0),
            Stretch = Stretch.Uniform,
        });
        Grid.SetColumn(dragRegion, 1);
        bar.Children.Add(dragRegion);
        Grid.SetColumn(buttons, 2);
        bar.Children.Add(buttons);
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#181818")),
            BorderBrush = new SolidColorBrush(Color.Parse("#333333")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar,
        };
    }

    private static Button CaptionButton(StreamGeometry geometry, string tip)
    {
        var button = new Button
        {
            Content = new PathIcon { Data = geometry, Width = 14, Height = 14 },
        };
        button.Classes.Add("caption");
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static void InstallResizeLayer(Window window)
    {
        if (ResizeLayers.TryGetValue(window, out _) || window.Content is not Control content)
            return;
        ResizeLayers.Add(window, new object());

        window.Content = null;
        var host = new Grid();
        host.Children.Add(content);
        Border[] grips =
        [
            ResizeGrip(window, WindowEdge.West, StandardCursorType.SizeWestEast,
                HorizontalAlignment.Left, VerticalAlignment.Stretch, ResizeBorderThickness, double.NaN),
            ResizeGrip(window, WindowEdge.East, StandardCursorType.SizeWestEast,
                HorizontalAlignment.Right, VerticalAlignment.Stretch, ResizeBorderThickness, double.NaN),
            ResizeGrip(window, WindowEdge.North, StandardCursorType.SizeNorthSouth,
                HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, ResizeBorderThickness),
            ResizeGrip(window, WindowEdge.South, StandardCursorType.SizeNorthSouth,
                HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, ResizeBorderThickness),
            ResizeGrip(window, WindowEdge.NorthWest, StandardCursorType.TopLeftCorner,
                HorizontalAlignment.Left, VerticalAlignment.Top, ResizeCornerSize, ResizeCornerSize),
            ResizeGrip(window, WindowEdge.NorthEast, StandardCursorType.TopRightCorner,
                HorizontalAlignment.Right, VerticalAlignment.Top, ResizeCornerSize, ResizeCornerSize),
            ResizeGrip(window, WindowEdge.SouthWest, StandardCursorType.BottomLeftCorner,
                HorizontalAlignment.Left, VerticalAlignment.Bottom, ResizeCornerSize, ResizeCornerSize),
            ResizeGrip(window, WindowEdge.SouthEast, StandardCursorType.BottomRightCorner,
                HorizontalAlignment.Right, VerticalAlignment.Bottom, ResizeCornerSize, ResizeCornerSize),
        ];
        foreach (Border grip in grips)
        {
            grip.ZIndex = int.MaxValue;
            host.Children.Add(grip);
        }
        window.Content = host;

        void RefreshHitTesting()
        {
            bool enabled = window.CanResize && window.WindowState == WindowState.Normal;
            foreach (Border grip in grips)
                grip.IsHitTestVisible = enabled;
        }

        window.PropertyChanged += (_, args) =>
        {
            if (args.Property == Window.CanResizeProperty || args.Property == Window.WindowStateProperty)
                RefreshHitTesting();
        };
        RefreshHitTesting();
    }

    private static Border ResizeGrip(Window window, WindowEdge edge, StandardCursorType cursor,
        HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment,
        double width, double height)
    {
        var grip = new Border
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            Width = width,
            Height = height,
            Cursor = new Cursor(cursor),
        };
        grip.PointerPressed += (_, args) =>
        {
            if (!window.CanResize || window.WindowState != WindowState.Normal ||
                args.GetCurrentPoint(window).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
                return;
            window.BeginResizeDrag(edge, args);
            args.Handled = true;
        };
        return grip;
    }

    private static void ApplyWindowsChrome(Window window)
    {
        nint handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (handle == 0)
            return;

        uint cornerPreference = DwmCornerPreferenceRound;
        DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref cornerPreference, sizeof(uint));
        uint borderColor = DwmColorNone;
        DwmSetWindowAttribute(handle, DwmBorderColor, ref borderColor, sizeof(uint));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle, int attribute, ref uint attributeValue, int attributeSize);
}
