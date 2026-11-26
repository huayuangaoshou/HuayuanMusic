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
    private int renderedLine = -1;
    private bool showTranslation;
    private readonly Border tools;
    private readonly StackPanel toolButtons;
    private readonly StackPanel labels;
    private readonly PathShape playIcon;
    private readonly Window reference;
    private static readonly Geometry PlayIcon = Geometry.Parse("M8,5 L19,12 L8,19 Z");
    private static readonly Geometry PauseIcon = Geometry.Parse("M9,5 L9,19 M15,5 L15,19");
    private readonly DesktopLyricMousePassthroughService passthrough = new();
    private readonly DispatcherTimer renderClock = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer hideToolsDelay = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer saveDelay = new() { Interval = TimeSpan.FromMilliseconds(360) };
    private readonly DispatcherTimer nativeInputClock = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private IReadOnlyList<LyricLine> lyrics = [];
    private string empty = "播放一首歌曲后显示歌词";
    private double anchorPosition;
    private long anchorTimestamp = Stopwatch.GetTimestamp();
    private bool running;
    private double fontSize;
    private double lyricWidth;
    private double toolbarHeight;
    private bool placed;
    private bool windowsInputEnabled = true;
    private int changedInputSamples;

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
            Add(Icon(Geometry.Parse("M8,11 L8,8 A4,4 0 0 1 16,8 L16,11 M5.5,11 L18.5,11 L18.5,19 L5.5,19 Z")), "锁定桌面歌词并穿透鼠标", () => SetLocked(true));
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
            Background = null,
            Opacity = 0, IsHitTestVisible = false
        };
        tools.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(170), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }];
        labels = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(ShadowGutter), Background = null, Spacing = 4
        };
        labels.Children.Add(current); labels.Children.Add(translation); labels.Children.Add(next);
        Grid.SetRow(labels, 1);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Background = null };
        layout.Children.Add(tools); layout.Children.Add(labels);
        Content = layout;
        if (OperatingSystem.IsWindows())
        {
            // A borderless popup avoids the Win32 minimum size of an overlapped window.
            const uint popupStyle = 0x80000000;
            Win32Properties.AddWindowStylesCallback(this, (style, extendedStyle) => (style | popupStyle, extendedStyle));
            Win32Properties.AddWndProcHookCallback(this, HitTestWindow);
        }
        hideToolsDelay.Tick += (_, _) => { hideToolsDelay.Stop(); ShowTools(false); };
        layout.AddHandler(PointerPressedEvent, DragWindow, RoutingStrategies.Bubble);

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
            if (preferences.Locked) SetLocked(true);
            if (OperatingSystem.IsWindows())
            {
                if (!Locked)
                {
                    passthrough.Apply(this, DesktopLyricHitTestLayout.Transparent);
                    windowsInputEnabled = false;
                }
                nativeInputClock.Start();
            }
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
        PositionChanged += (_, _) => ScheduleSave();
        renderClock.Tick += (_, _) => RenderLyrics();
        saveDelay.Tick += (_, _) => { saveDelay.Stop(); PreferencesChanged?.Invoke(Capture(true)); };
        nativeInputClock.Tick += (_, _) => UpdateWindowsInput();
        Closed += (_, _) =>
        {
            renderClock.Stop();
            saveDelay.Stop();
            hideToolsDelay.Stop();
            nativeInputClock.Stop();
            current.SetLine(null);
            translation.SetLine(null);
            next.SetLine(null);
        };
    }

    private static DesktopLyricLine CreateLine(IBrush foreground) => new(foreground);

    private IntPtr HitTestWindow(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0084 || Locked) return IntPtr.Zero;
        var raw = lParam.ToInt64();
        var point = this.PointToClient(new PixelPoint(unchecked((short)raw), unchecked((short)(raw >> 16))));
        if (HitsLyrics(point) || HitsToolbar(point)) return IntPtr.Zero;
        handled = true;
        return new IntPtr(-1); // HTTRANSPARENT
    }

    private bool HitsLyrics(Point point) =>
        Hits(point, current) || Hits(point, translation) || Hits(point, next);

    private bool HitsToolbar(Point point) => tools.IsHitTestVisible && toolButtons.Children.Any(control =>
        control.IsVisible && control.TranslatePoint(default, this) is { } origin &&
        new Rect(origin, control.Bounds.Size).Contains(point));

    private bool Hits(Point point, DesktopLyricLine line) => line.IsVisible &&
        line.TranslatePoint(default, this) is { } origin &&
        line.HitsInk(new Point(point.X - origin.X, point.Y - origin.Y));

    private void UpdateWindowsInput()
    {
        if (Locked || !IsVisible || !GetCursorPos(out var screenPoint))
        {
            changedInputSamples = 0;
            return;
        }
        // Keep the OS target stable from press through release, including native window dragging.
        if ((GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0)
        {
            changedInputSamples = 0;
            return;
        }
        var point = this.PointToClient(new PixelPoint(screenPoint.X, screenPoint.Y));
        bool hot = HitsLyrics(point) || HitsToolbar(point);
        if (hot == windowsInputEnabled)
        {
            changedInputSamples = 0;
            return;
        }
        if (++changedInputSamples < 2) return;
        changedInputSamples = 0;
        windowsInputEnabled = hot;
        // HTTRANSPARENT alone does not guarantee routing to an unrelated process.
        passthrough.Apply(this, hot ? DesktopLyricHitTestLayout.FullWindow : DesktopLyricHitTestLayout.Transparent);
        if (hot)
        {
            hideToolsDelay.Stop();
            ShowTools(true);
        }
        else
        {
            hideToolsDelay.Start();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    internal DesktopLyricOptions Capture(bool visible)
    {
        var scale = RenderScaling;
        return new(visible, Locked,
            Position.X + ToPixels(Width / 2, scale) - ToPixels(lyricWidth / 2, scale),
            Position.Y + ToPixels(toolbarHeight, scale), fontSize);
    }

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
        translationButton.IsVisible = lyrics.Any(line => !string.IsNullOrWhiteSpace(line.Translation));
        RenderLyrics();
    }

    internal void SetLocked(bool locked)
    {
        if (!passthrough.IsSupported || locked == Locked) return;
        Locked = locked;
        windowsInputEnabled = !locked && !OperatingSystem.IsWindows();
        passthrough.Apply(this, windowsInputEnabled ? DesktopLyricHitTestLayout.FullWindow : DesktopLyricHitTestLayout.Transparent);
        changedInputSamples = 0;
        hideToolsDelay.Stop();
        ShowTools(false);
        ScheduleSave();
        LockChanged?.Invoke();
        Program.Log("R2_DESKTOP_LYRIC_LOCK", new { locked });
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
            ? new PixelPoint(saved.X - ToPixels(Width / 2, screen.Scaling) + ToPixels(lyricWidth / 2, screen.Scaling),
                saved.Y - ToPixels(toolbarHeight, screen.Scaling))
            : new PixelPoint(area.X + (area.Width - width) / 2, area.Bottom - height - 70);
        Position = new PixelPoint(Math.Clamp(position.X, area.X, Math.Max(area.X, area.Right - width)),
            Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
    }

    private void ShowTools(bool shown)
    {
        tools.IsHitTestVisible = shown && !Locked;
        tools.Opacity = tools.IsHitTestVisible ? 1 : 0;
    }

    private void DragWindow(object? sender, PointerPressedEventArgs args)
    {
        var point = args.GetCurrentPoint(this);
        if (Locked || !point.Properties.IsLeftButtonPressed || !HitsLyrics(point.Position)) return;
        BeginMoveDrag(args);
        args.Handled = true;
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
        tools.InvalidateMeasure();
        tools.Measure(Size.Infinity);
        const double gutter = 2 * ShadowGutter;
        // The label stack's DesiredSize already includes its margin.
        double newLyricWidth = Math.Max(gutter, Math.Ceiling(labels.DesiredSize.Width));
        double newToolbarHeight = Math.Ceiling(tools.DesiredSize.Height);
        double width = Math.Max(newLyricWidth, Math.Ceiling(tools.DesiredSize.Width) + gutter);
        double height = Math.Max(gutter, Math.Ceiling(labels.DesiredSize.Height)) + newToolbarHeight;
        if (width == Width && height == Height && newToolbarHeight == toolbarHeight)
        {
            lyricWidth = newLyricWidth;
            return;
        }

        // Keep the horizontal centre and top of the lyric stack fixed as its size changes.
        var position = placed ? Position : default;
        double oldWidth = Width;
        double oldToolbarHeight = toolbarHeight;
        lyricWidth = newLyricWidth;
        toolbarHeight = newToolbarHeight;
        Width = width;
        Height = height;
        if (placed)
        {
            var scale = RenderScaling;
            Position = new PixelPoint(position.X + ToPixels(oldWidth / 2, scale) - ToPixels(width / 2, scale),
                position.Y + ToPixels(oldToolbarHeight, scale) - ToPixels(toolbarHeight, scale));
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
