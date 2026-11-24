using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaLyrics;
using KugouAvaloniaPlayer.Services.DesktopLyric;

namespace HuayuanPreview.R2;

internal sealed class DesktopLyricWindow : Window
{
    private readonly DesktopLyricLine current = CreateLine(Brushes.White);
    private readonly DesktopLyricLine translation = CreateLine(new SolidColorBrush(DesignTokens.DarkSub));
    private readonly DesktopLyricLine next = CreateLine(new SolidColorBrush(DesignTokens.DarkText));
    private readonly Button translationButton;
    private int renderedLine = -1;
    private bool showTranslation;
    private readonly Border tools;
    private readonly PathIcon playIcon;
    private readonly Window reference;
    private static readonly Geometry PlayIcon = Geometry.Parse("M4,2 L16,9 L4,16 Z");
    private static readonly Geometry PauseIcon = Geometry.Parse("M4,2 H7 V16 H4 Z M11,2 H14 V16 H11 Z");
    private readonly DesktopLyricMousePassthroughService passthrough = new();
    private readonly DispatcherTimer renderClock = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer hideToolsDelay = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer saveDelay = new() { Interval = TimeSpan.FromMilliseconds(360) };
    private IReadOnlyList<LyricLine> lyrics = [];
    private string empty = "播放一首歌曲后显示歌词";
    private double anchorPosition;
    private long anchorTimestamp = Stopwatch.GetTimestamp();
    private bool running;
    private double fontSize;

    internal bool Locked { get; private set; }
    internal event Action<DesktopLyricOptions>? PreferencesChanged;
    internal event Action? LockChanged;

    internal DesktopLyricWindow(Window reference, CorePresentation player, DesktopLyricOptions preferences,
        bool showTranslation, Action toggleTranslation)
    {
        this.reference = reference;
        this.showTranslation = showTranslation;
        Title = "HuayuanMusic 桌面歌词";
        Width = 760;
        Height = 178;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;

        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        Button Add(object label, string accessible, Action action)
        {
            var button = new Button
            {
                Content = label, Padding = new Thickness(8, 3), MinHeight = 28,
                Foreground = new SolidColorBrush(DesignTokens.DarkText),
                Background = new SolidColorBrush(Color.FromArgb(0x38, DesignTokens.BlueMid.R, DesignTokens.BlueMid.G, DesignTokens.BlueMid.B))
            };
            AutomationProperties.SetName(button, accessible);
            ToolTip.SetTip(button, accessible);
            button.Click += (_, _) => action();
            commands.Children.Add(button);
            return button;
        }
        static PathIcon Icon(string path) => new() { Width = 13, Height = 13, Data = Geometry.Parse(path), Foreground = new SolidColorBrush(DesignTokens.DarkText) };
        var grip = Icon("M1,3 H17 V5 H1 Z M1,8 H17 V10 H1 Z M1,13 H17 V15 H1 Z");
        grip.Margin = new Thickness(7,0,6,0);
        grip.Cursor = new Cursor(StandardCursorType.SizeAll);
        grip.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(grip, "拖动桌面歌词");
        commands.Children.Add(grip);
        Add(Icon("M3,2 H5 V18 H3 Z M17,2 L6,10 L17,18 Z"), "桌面歌词上一首", () => player.PlayPreviousCommand.Execute(null));
        playIcon = Icon("M4,2 L16,9 L4,16 Z");
        Add(playIcon, "桌面歌词播放或暂停", () => player.TogglePlayPauseCommand.Execute(null));
        Add(Icon("M3,2 L14,10 L3,18 Z M15,2 H17 V18 H15 Z"), "桌面歌词下一首", () => player.PlayNextCommand.Execute(null));
        Add("A−", "减小歌词字号", () => AdjustFont(-2));
        Add("A+", "增大歌词字号", () => AdjustFont(2));
        translationButton = Add("译", "显示或隐藏桌面歌词译文", toggleTranslation);
        translationButton.IsVisible = false;
        if (passthrough.IsSupported)
            Add(Icon("M4,8 H14 V17 H4 Z M6,8 V5 C6,1 12,1 12,5 V8 H10 V5 C10,3 8,3 8,5 V8 Z"), "锁定桌面歌词并穿透鼠标", () => SetLocked(true));
        Add(Icon("M4,2 L9,7 L14,2 L16,4 L11,9 L16,14 L14,16 L9,11 L4,16 L2,14 L7,9 L2,4 Z"), "关闭桌面歌词", Close);

        tools = new Border
        {
            Child = commands, Padding = new Thickness(7, 4), CornerRadius = new CornerRadius(22),
            VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(DesignTokens.LightSurface),
            BorderBrush = new SolidColorBrush(DesignTokens.LightLine),
            BorderThickness = new Thickness(1), BoxShadow = BoxShadows.Parse("0 8 24 0 #140F172A"),
            Opacity = 0, IsHitTestVisible = false
        };
        tools.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(170), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }];
        var labels = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4
        };
        labels.Children.Add(current); labels.Children.Add(translation); labels.Children.Add(next);
        Grid.SetRow(labels, 1);
        var layout = new Grid { RowDefinitions = new RowDefinitions("38,*"), Background = null };
        layout.Children.Add(tools); layout.Children.Add(labels);
        Content = layout;
        if (OperatingSystem.IsWindows())
            Win32Properties.AddWndProcHookCallback(this, HitTestWindow);
        layout.PointerEntered += (_, _) => { hideToolsDelay.Stop(); ShowTools(true); };
        layout.PointerExited += (_, _) => hideToolsDelay.Start();
        hideToolsDelay.Tick += (_, _) => { hideToolsDelay.Stop(); ShowTools(false); };
        layout.AddHandler(PointerPressedEvent, DragWindow, RoutingStrategies.Bubble);

        fontSize = Math.Clamp(preferences.FontSize == 30 ? DesktopLyricOptions.DefaultFontSize : preferences.FontSize, 18, 46);
        AdjustFont(0);
        Opened += (_, _) =>
        {
            Place(preferences.X is int x && preferences.Y is int y ? new PixelPoint(x, y) : null);
            new DesktopLyricWindowChromeService().HideFromWindowSwitcher(this);
            if (preferences.Locked) SetLocked(true);
            UpdateFont();
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
            tools.Background = new SolidColorBrush(dark ? DesignTokens.DarkSurface : DesignTokens.LightSurface);
            tools.BorderBrush = new SolidColorBrush(dark ? DesignTokens.DarkLine : DesignTokens.LightLine);
            tools.BoxShadow = BoxShadows.Parse(dark ? "0 8 24 0 #50101726" : "0 8 24 0 #140F172A");
            var foreground = new SolidColorBrush(dark ? DesignTokens.DarkText : DesignTokens.LightText);
            foreach (var button in commands.Children.OfType<Button>())
            {
                button.Foreground = foreground;
                if (button.Content is PathIcon icon) icon.Foreground = foreground;
            }
            grip.Foreground = foreground;
        }
        RefreshToolbarTheme();
        ActualThemeVariantChanged += (_, _) => RefreshToolbarTheme();
        PositionChanged += (_, _) => ScheduleSave();
        renderClock.Tick += (_, _) => RenderLyrics();
        saveDelay.Tick += (_, _) => { saveDelay.Stop(); PreferencesChanged?.Invoke(Capture(true)); };
        Closed += (_, _) =>
        {
            renderClock.Stop();
            saveDelay.Stop();
            hideToolsDelay.Stop();
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
        bool Hits(Control control) => control.IsVisible && control.TranslatePoint(default, this) is { } origin &&
            new Rect(origin, control.Bounds.Size).Contains(point);
        if (Hits(current) || Hits(translation) || Hits(next) ||
            (tools.IsHitTestVisible && Hits(tools))) return IntPtr.Zero;
        handled = true;
        return new IntPtr(-1); // HTTRANSPARENT
    }

    internal DesktopLyricOptions Capture(bool visible) => new(visible, Locked, Position.X, Position.Y, fontSize);

    internal void Sync(PlaybackSnapshot snapshot)
    {
        anchorPosition = snapshot.Position;
        anchorTimestamp = Stopwatch.GetTimestamp();
        running = snapshot.Playing;
        playIcon.Data = running ? PauseIcon : PlayIcon;
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
        passthrough.Apply(this, locked ? DesktopLyricHitTestLayout.Transparent : DesktopLyricHitTestLayout.FullWindow);
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
            ? saved : new PixelPoint(area.X + (area.Width - width) / 2, area.Bottom - height - 70);
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
        if (Locked || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            args.Source is Visual visual && (visual is Button || visual.GetVisualAncestors().OfType<Button>().Any())) return;
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
        }
        current.SetPosition(position);
        translation.SetPosition(position);
    }
}
