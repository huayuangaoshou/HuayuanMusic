using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using PathShape = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaLyrics;
using KugouAvaloniaPlayer.Services.DesktopLyric;

namespace HuayuanPreview.R2;

internal sealed class DesktopLyricWindow : Window
{
    internal const double ShadowGutter = 12;

    private readonly DesktopLyricLine current = CreateLine(Brushes.White);
    private readonly DesktopLyricLine translation = CreateLine(new SolidColorBrush(DesignTokens.DarkSub));
    private readonly DesktopLyricLine next = CreateLine(new SolidColorBrush(DesignTokens.DarkText));
    private readonly Button translationButton;
    private readonly Button? lockButton;
    private bool hasTranslation;
    private int renderedLine = -1;
    private bool showTranslation;
    private readonly Border tools;
    private readonly Window toolbarWindow;
    private readonly StackPanel toolButtons;
    private readonly StackPanel labels;
    private readonly PathShape playIcon;
    private readonly Window reference;
    private static readonly Geometry PlayIcon = Geometry.Parse("M8,5 L19,12 L8,19 Z");
    private static readonly Geometry PauseIcon = Geometry.Parse("M9,5 L9,19 M15,5 L15,19");
    private static readonly Geometry LockIcon = Geometry.Parse("M8,11 L8,8 A4,4 0 0 1 16,8 L16,11 M5.5,11 L18.5,11 L18.5,19 L5.5,19 Z");
    private static readonly Geometry UnlockIcon = Geometry.Parse("M8,11 L8,8 A4,4 0 0 1 16,8 L16,6.5 M5.5,11 L18.5,11 L18.5,19 L5.5,19 Z");
    private readonly DesktopLyricMousePassthroughService passthrough = new();
    private readonly DispatcherTimer renderClock = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private static readonly TimeSpan HideToolsDelay = TimeSpan.FromMilliseconds(200);
    private readonly DispatcherTimer saveDelay = new() { Interval = TimeSpan.FromMilliseconds(360) };
    private readonly DispatcherTimer nativeInputClock = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private IReadOnlyList<LyricLine> lyrics = [];
    private string empty = "播放一首歌曲后显示歌词";
    private double anchorPosition;
    private long anchorTimestamp = Stopwatch.GetTimestamp();
    private bool running;
    private double fontSize;
    private bool placed;
    private long? outsideToolbarSince;
    private IPointer? toolbarDrag;
    private PixelPoint dragStart;
    private PixelPoint dragOrigin;
    private bool toolbarClosed;

    internal bool Locked { get; private set; }
    internal event Action<DesktopLyricOptions>? PreferencesChanged;
    internal event Action? LockChanged;

    internal DesktopLyricWindow(Window reference, CorePresentation player, DesktopLyricOptions preferences,
        bool showTranslation, Action toggleTranslation)
    {
        this.reference = reference;
        this.showTranslation = showTranslation;
        Title = "HuayuanMusic 桌面歌词";
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = null;

        var commands = toolButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Background = null };
        var hoverStyle = new Style(s => s.OfType<Button>().Class(":pointerover").Template()
            .OfType<ContentPresenter>().Name("PART_ContentPresenter"));
        hoverStyle.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, new DynamicResourceExtension("LyricToolbarHover")));
        commands.Styles.Add(hoverStyle);
        Button Add(object label, string accessible, Action action)
        {
            var button = new Button
            {
                Content = label, Padding = new Thickness(0), MinWidth = 32, MinHeight = 32,
                CornerRadius = new CornerRadius(999), Background = Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(button, accessible);
            ToolTip.SetTip(button, accessible);
            button.Click += (_, _) => action();
            commands.Children.Add(button);
            return button;
        }
        static PathShape Icon(Geometry data) => new()
        {
            Data = data, Width = 16, Height = 16, Stretch = Stretch.Uniform, Fill = null,
            StrokeThickness = 1.8, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        Add(Icon(Geometry.Parse("M19,5 L9,12 L19,19 Z M7,5 L7,19")), "桌面歌词上一首", () => player.PlayPreviousCommand.Execute(null));
        playIcon = Icon(PlayIcon);
        Add(playIcon, "桌面歌词播放或暂停", () => player.TogglePlayPauseCommand.Execute(null));
        Add(Icon(Geometry.Parse("M5,5 L15,12 L5,19 Z M17,5 L17,19")), "桌面歌词下一首", () => player.PlayNextCommand.Execute(null));
        Add("A−", "减小歌词字号", () => AdjustFont(-2));
        Add("A+", "增大歌词字号", () => AdjustFont(2));
        translationButton = Add("译", "显示或隐藏桌面歌词译文", toggleTranslation);
        translationButton.IsVisible = false;
        if (passthrough.IsSupported)
            lockButton = Add(Icon(LockIcon), "锁定桌面歌词并穿透鼠标", () => SetLocked(!Locked));
        Add(Icon(Geometry.Parse("M6,6 L18,18 M18,6 L6,18")), "关闭桌面歌词", Close);

        var toolSurface = new Border
        {
            CornerRadius = new CornerRadius(22),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false
        };
        commands.Margin = new Thickness(8, 5);
        tools = new Border
        {
            Child = new Grid { Children = { toolSurface, commands } },
            VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center,
            Background = Brushes.Transparent,
            Opacity = 0
        };
        tools.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(170), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }];
        labels = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(ShadowGutter), Background = null, Spacing = 4
        };
        labels.Children.Add(current); labels.Children.Add(translation); labels.Children.Add(next);
        toolbarWindow = new Window
        {
            Title = "桌面歌词工具条", Content = tools, Topmost = true, ShowActivated = false,
            ShowInTaskbar = false, WindowDecorations = WindowDecorations.None, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.Manual, SizeToContent = SizeToContent.WidthAndHeight,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent], Background = null
        };
        var layout = new Grid { Background = null };
        layout.Children.Add(labels);
        Content = layout;
        if (OperatingSystem.IsWindows())
        {
            // A borderless popup avoids the Win32 minimum size of an overlapped window.
            const uint popupStyle = 0x80000000;
            Win32Properties.AddWindowStylesCallback(this, (style, extendedStyle) => (style | popupStyle, extendedStyle));
        }
        tools.AddHandler(PointerPressedEvent, DragFromToolbar, RoutingStrategies.Bubble);
        tools.PointerMoved += MoveFromToolbar;
        tools.PointerReleased += (_, args) => { if (toolbarDrag == args.Pointer) EndToolbarDrag(); };
        tools.PointerCaptureLost += (_, _) => toolbarDrag = null;
        toolbarWindow.Resized += (_, _) => PositionToolbar();
        toolbarWindow.Closed += (_, _) => toolbarClosed = true;

        fontSize = Math.Clamp(preferences.FontSize == 30 ? DesktopLyricOptions.DefaultFontSize : preferences.FontSize, 18, 46);
        AdjustFont(0);
        RenderLyrics();
        Opened += (_, _) =>
        {
            UpdateFont();
            RenderLyrics();
            Place(preferences.X is int x && preferences.Y is int y ? new PixelPoint(x, y) : null);
            placed = true;
            new DesktopLyricWindowChromeService().HideFromWindowSwitcher(this);
            passthrough.Apply(this, DesktopLyricHitTestLayout.Transparent);
            if (preferences.Locked) SetLocked(true);
            if (OperatingSystem.IsWindows())
                nativeInputClock.Start();
            if (running) renderClock.Start();
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsVisible) return;
                PreferencesChanged?.Invoke(Capture(true));
                Program.Log("R2_DESKTOP_LYRIC_OPEN", new
                {
                    transparent = ActualTransparencyLevel == WindowTransparencyLevel.Transparent,
                    topmost = Topmost, taskbar = ShowInTaskbar
                });
            }, DispatcherPriority.Loaded);
        };
        void RefreshToolbarTheme()
        {
            var dark = ActualThemeVariant == ThemeVariant.Dark;
            toolbarWindow.RequestedThemeVariant = ActualThemeVariant;
            toolSurface.Background = new SolidColorBrush(dark ? DesignTokens.DarkSurface : DesignTokens.LightSurface);
            toolSurface.BorderBrush = new SolidColorBrush(dark ? DesignTokens.DarkLine : DesignTokens.LightLine);
            toolSurface.BoxShadow = BoxShadows.Parse(dark ? "0 8 24 0 #50101726" : "0 8 24 0 #140F172A");
            var foreground = new SolidColorBrush(dark ? DesignTokens.DarkText : DesignTokens.LightText);
            var hoverBrush = new SolidColorBrush(DesignTokens.AccentStrong, .12);
            commands.Resources["LyricToolbarHover"] = hoverBrush;
            foreach (var button in commands.Children.OfType<Button>())
            {
                button.Foreground = foreground;
                if (button.Content is PathShape icon) icon.Stroke = foreground;
            }
        }
        RefreshToolbarTheme();
        ActualThemeVariantChanged += (_, _) => RefreshToolbarTheme();
        PositionChanged += (_, _) => { PositionToolbar(); ScheduleSave(); };
        SizeChanged += (_, _) => PositionToolbar();
        labels.SizeChanged += (_, _) => PositionToolbar();
        renderClock.Tick += (_, _) => RenderLyrics();
        saveDelay.Tick += (_, _) => { saveDelay.Stop(); PreferencesChanged?.Invoke(Capture(true)); };
        nativeInputClock.Tick += (_, _) => UpdateToolbarHover();
        Closed += (_, _) =>
        {
            renderClock.Stop();
            saveDelay.Stop();
            nativeInputClock.Stop();
            if (!toolbarClosed) toolbarWindow.Close();
            current.SetLine(null);
            translation.SetLine(null);
            next.SetLine(null);
        };
    }

    private static DesktopLyricLine CreateLine(IBrush foreground) => new(foreground);

    private static bool ContainsScreenPoint(Control control, PixelPoint point) =>
        control.IsVisible && control.IsAttachedToVisualTree() &&
        new Rect(control.Bounds.Size).Contains(control.PointToClient(point));

    private void UpdateToolbarHover()
    {
        if (!IsVisible || !GetCursorPos(out var screenPoint)) return;
        if ((GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0)
        {
            outsideToolbarSince = null;
            return;
        }
        var point = new PixelPoint(screenPoint.X, screenPoint.Y);
        // The label stack includes the gaps between its visible lines.
        if (ContainsScreenPoint(labels, point) || (toolbarWindow.IsVisible && ContainsScreenPoint(tools, point)))
        {
            outsideToolbarSince = null;
            ShowTools(true);
        }
        else if (toolbarWindow.IsVisible)
        {
            outsideToolbarSince ??= Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(outsideToolbarSince.Value) >= HideToolsDelay)
                ShowTools(false);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    internal DesktopLyricOptions Capture(bool visible) => new(visible, Locked, Position.X, Position.Y, fontSize);

    internal void Sync(PlaybackSnapshot snapshot)
    {
        anchorPosition = snapshot.Position;
        anchorTimestamp = Stopwatch.GetTimestamp();
        running = snapshot.Playing;
        playIcon.Data = running ? PauseIcon : PlayIcon;
        playIcon.StrokeThickness = running ? 2.2 : 1.8;
        if (running) renderClock.Start(); else renderClock.Stop();
        RenderLyrics();
    }

    internal void SetLyrics(IReadOnlyList<LyricLine> lines, string message)
    {
        lyrics = lines;
        empty = message;
        renderedLine = -1;
        hasTranslation = lyrics.Any(line => !string.IsNullOrWhiteSpace(line.Translation));
        ApplyLockMode();
        RenderLyrics();
    }

    internal void SetLocked(bool locked)
    {
        if (!passthrough.IsSupported || locked == Locked) return;
        Locked = locked;
        if (locked) EndToolbarDrag();
        ApplyLockMode();
        ScheduleSave();
        LockChanged?.Invoke();
        Program.Log("R2_DESKTOP_LYRIC_LOCK", new { locked });
    }

    private void ApplyLockMode()
    {
        foreach (var child in toolButtons.Children)
            child.IsVisible = child == lockButton || (!Locked && (child != translationButton || hasTranslation));
        if (lockButton?.Content is PathShape icon)
        {
            icon.Data = Locked ? UnlockIcon : LockIcon;
            string name = Locked ? "解锁桌面歌词" : "锁定桌面歌词并穿透鼠标";
            AutomationProperties.SetName(lockButton, name);
            ToolTip.SetTip(lockButton, name);
        }
    }

    internal void ResetAppearance()
    {
        fontSize = DesktopLyricOptions.DefaultFontSize;
        AdjustFont(0);
        Place(null);
        ScheduleSave();
    }

    private void Place(PixelPoint? requested)
    {
        var screen = (requested is { } point ? reference.Screens.All.FirstOrDefault(item => item.WorkingArea.Contains(point)) : null)
            ?? reference.Screens.ScreenFromWindow(reference) ?? reference.Screens.Primary;
        if (screen == null) return;
        var area = screen.WorkingArea;
        var width = (int)(Width * screen.Scaling);
        var height = (int)(Height * screen.Scaling);
        var position = requested is { } saved && area.Contains(saved)
            ? saved
            : new PixelPoint(area.X + (area.Width - width) / 2, area.Bottom - height - 70);
        Position = new PixelPoint(Math.Clamp(position.X, area.X, Math.Max(area.X, area.Right - width)),
            Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
    }

    private void ShowTools(bool shown)
    {
        if (toolbarClosed || toolbarWindow.IsVisible == shown) return;
        if (shown)
        {
            toolbarWindow.Show(this);
            PositionToolbar();
            tools.Opacity = 1;
        }
        else
        {
            tools.Opacity = 0;
            toolbarWindow.Hide();
        }
    }

    private void PositionToolbar()
    {
        if (!toolbarWindow.IsVisible || !labels.IsAttachedToVisualTree()) return;
        var anchor = labels.PointToScreen(new Point(labels.Bounds.Width / 2, 0));
        var scale = toolbarWindow.RenderScaling;
        var width = ToPixels(toolbarWindow.ClientSize.Width, scale);
        var height = ToPixels(toolbarWindow.ClientSize.Height, scale);
        var target = new PixelPoint(anchor.X - width / 2, anchor.Y - height);
        if (Screens.ScreenFromPoint(anchor) is { } screen)
        {
            var area = screen.WorkingArea;
            int y = target.Y < area.Y ? labels.PointToScreen(new Point(0, labels.Bounds.Height)).Y : target.Y;
            target = new PixelPoint(Math.Clamp(target.X, area.X, Math.Max(area.X, area.Right - width)),
                Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - height)));
        }
        if (toolbarWindow.Position != target) toolbarWindow.Position = target;
    }

    private void DragFromToolbar(object? sender, PointerPressedEventArgs args)
    {
        if (Locked || !args.GetCurrentPoint(tools).Properties.IsLeftButtonPressed ||
            args.Source is Visual source && (source is Button || source.FindAncestorOfType<Button>() != null)) return;
        dragStart = tools.PointToScreen(args.GetPosition(tools));
        dragOrigin = Position;
        toolbarDrag = args.Pointer;
        toolbarDrag.Capture(tools);
        args.Handled = true;
    }

    private void MoveFromToolbar(object? sender, PointerEventArgs args)
    {
        if (args.Pointer != toolbarDrag) return;
        if (Locked || !args.GetCurrentPoint(tools).Properties.IsLeftButtonPressed)
        {
            EndToolbarDrag();
            return;
        }
        var point = tools.PointToScreen(args.GetPosition(tools));
        Position = dragOrigin + (point - dragStart);
        args.Handled = true;
    }

    private void EndToolbarDrag()
    {
        var pointer = toolbarDrag;
        toolbarDrag = null;
        pointer?.Capture(null);
    }

    private void AdjustFont(double difference)
    {
        fontSize = Math.Clamp(fontSize + difference, 18, 46);
        UpdateFont();
        if (difference != 0) ScheduleSave();
    }

    private void UpdateFont()
    {
        current.SetFont(FontFamily, fontSize);
        translation.SetFont(FontFamily, Math.Max(14, fontSize * .58));
        next.SetFont(FontFamily, Math.Max(16, fontSize * .66));
        ResizeToLyrics();
    }

    private static int ToPixels(double value, double scale) => (int)Math.Round(value * scale);

    private void ResizeToLyrics()
    {
        labels.InvalidateMeasure();
        labels.Measure(Size.Infinity);
        const double gutter = 2 * ShadowGutter;
        // The label stack's DesiredSize already includes its margin.
        double width = Math.Max(gutter, Math.Ceiling(labels.DesiredSize.Width));
        double height = Math.Max(gutter, Math.Ceiling(labels.DesiredSize.Height));
        if (width == Width && height == Height) return;

        // Keep the horizontal centre and top of the lyric stack fixed as its size changes.
        var position = placed ? Position : default;
        double oldWidth = Width;
        Width = width;
        Height = height;
        if (placed)
        {
            var scale = RenderScaling;
            Position = new PixelPoint(position.X + ToPixels(oldWidth / 2, scale) - ToPixels(width / 2, scale),
                position.Y);
        }
    }

    internal void SetShowTranslation(bool visible)
    {
        showTranslation = visible;
        renderedLine = -1;
        RenderLyrics();
    }

    private void ScheduleSave()
    {
        if (!IsVisible) return;
        saveDelay.Stop();
        saveDelay.Start();
    }

    private void RenderLyrics()
    {
        if (lyrics.Count == 0)
        {
            current.SetLine(empty);
            translation.SetLine(null);
            next.SetLine(null);
            ResizeToLyrics();
            return;
        }
        var position = anchorPosition + (running ? Stopwatch.GetElapsedTime(anchorTimestamp).TotalSeconds : 0);
        int first = 0, end = lyrics.Count;
        while (first < end)
        {
            int middle = first + (end - first) / 2;
            if (lyrics[middle].Start.TotalSeconds <= position) first = middle + 1;
            else end = middle;
        }
        int index = Math.Max(0, first - 1);
        if (index != renderedLine)
        {
            renderedLine = index;
            var line = lyrics[index];
            var following = index + 1 < lyrics.Count ? lyrics[index + 1] : null;
            var duration = line.Duration.TotalSeconds;
            if (duration <= 0) duration = following == null ? 3 : (following.Start - line.Start).TotalSeconds;
            duration = Math.Max(.1, duration);
            current.SetLine(line.Text, line.Start.TotalSeconds, duration, line.Words);
            translation.SetLine(showTranslation ? line.Translation : null, line.Start.TotalSeconds, duration, line.TranslationWords);
            next.SetLine(following?.Text);
            ResizeToLyrics();
        }
        current.SetPosition(position);
        translation.SetPosition(position);
    }
}
