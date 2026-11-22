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
using Avalonia.VisualTree;
using AvaloniaLyrics;
using KugouAvaloniaPlayer.Services.DesktopLyric;

namespace HuayuanPreview.R2;

internal sealed class DesktopLyricWindow : Window
{
    private readonly TextBlock current = LyricLabel();
    private readonly TextBlock next = LyricLabel();
    private readonly Border tools;
    private readonly Button play;
    private readonly DesktopLyricMousePassthroughService passthrough = new();
    private readonly DispatcherTimer renderClock = new() { Interval = TimeSpan.FromMilliseconds(33) };
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

    internal DesktopLyricWindow(Window reference, CorePresentation player, DesktopLyricOptions preferences)
    {
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
        Button Add(string label, string accessible, Action action)
        {
            var button = new Button
            {
                Content = label, Padding = new Thickness(8, 3), MinHeight = 28, Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.Parse("#403F5270"))
            };
            AutomationProperties.SetName(button, accessible);
            ToolTip.SetTip(button, accessible);
            button.Click += (_, _) => action();
            commands.Children.Add(button);
            return button;
        }
        Add("⏮", "桌面歌词上一首", () => player.PlayPreviousCommand.Execute(null));
        play = Add("▶", "桌面歌词播放或暂停", () => player.TogglePlayPauseCommand.Execute(null));
        Add("⏭", "桌面歌词下一首", () => player.PlayNextCommand.Execute(null));
        Add("A−", "减小歌词字号", () => AdjustFont(-2));
        Add("A+", "增大歌词字号", () => AdjustFont(2));
        if (passthrough.IsSupported) Add("锁定", "锁定桌面歌词并穿透鼠标", () => SetLocked(true));
        Add("×", "关闭桌面歌词", Close);

        tools = new Border
        {
            Child = commands, Padding = new Thickness(6, 4), CornerRadius = new CornerRadius(12),
            VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.Parse("#C51A253B")),
            Opacity = 0, IsHitTestVisible = false
        };
        tools.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(170) }];
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4, IsHitTestVisible = false };
        labels.Children.Add(current); labels.Children.Add(next);
        Grid.SetRow(labels, 1);
        var layout = new Grid { RowDefinitions = new RowDefinitions("38,*"), Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
        layout.Children.Add(tools); layout.Children.Add(labels);
        Content = layout;
        layout.PointerEntered += (_, _) => ShowTools(true);
        layout.PointerExited += (_, _) => ShowTools(false);
        layout.AddHandler(PointerPressedEvent, DragWindow, RoutingStrategies.Bubble);

        fontSize = Math.Clamp(preferences.FontSize, 18, 46);
        AdjustFont(0);
        Opened += (_, _) =>
        {
            var requested = preferences.X is int x && preferences.Y is int y ? new PixelPoint(x, y) : (PixelPoint?)null;
            var screen = (requested is { } point ? reference.Screens.All.FirstOrDefault(item => item.WorkingArea.Contains(point)) : null) ??
                reference.Screens.ScreenFromWindow(reference) ?? reference.Screens.Primary;
            if (screen != null)
            {
                var area = screen.WorkingArea;
                var width = (int)(Width * screen.Scaling);
                var height = (int)(Height * screen.Scaling);
                var position = requested is { } saved && area.Contains(saved)
                    ? saved : new PixelPoint(area.X + (area.Width - width) / 2, area.Bottom - height - 70);
                Position = new PixelPoint(Math.Clamp(position.X, area.X, Math.Max(area.X, area.Right - width)),
                    Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
            }
            new DesktopLyricWindowChromeService().HideFromWindowSwitcher(this);
            if (preferences.Locked) SetLocked(true);
            renderClock.Start();
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
        PositionChanged += (_, _) => ScheduleSave();
        renderClock.Tick += (_, _) => RenderLyrics();
        saveDelay.Tick += (_, _) => { saveDelay.Stop(); PreferencesChanged?.Invoke(Capture(true)); };
        Closed += (_, _) => { renderClock.Stop(); saveDelay.Stop(); };
    }

    private static TextBlock LyricLabel() => new()
    {
        Foreground = Brushes.White, FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
        FontWeight = FontWeight.Bold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(24, 0), Effect = new DropShadowEffect
        {
            BlurRadius = 5, OffsetX = 0, OffsetY = 1, Opacity = 1, Color = Color.Parse("#0B1020")
        }
    };

    internal DesktopLyricOptions Capture(bool visible) => new(visible, Locked, Position.X, Position.Y, fontSize);

    internal void Sync(PlaybackSnapshot snapshot)
    {
        anchorPosition = snapshot.Position;
        anchorTimestamp = Stopwatch.GetTimestamp();
        running = snapshot.Playing;
        play.Content = running ? "Ⅱ" : "▶";
        RenderLyrics();
    }

    internal void SetLyrics(IReadOnlyList<LyricLine> lines, string message)
    {
        lyrics = lines;
        empty = message;
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
        current.FontSize = fontSize;
        next.FontSize = Math.Max(16, fontSize * .72);
        next.Foreground = new SolidColorBrush(Color.Parse("#DADFFF"));
        if (difference != 0) ScheduleSave();
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
            current.Text = empty;
            next.Text = "";
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
        current.Text = lyrics[index].Text;
        next.Text = index + 1 < lyrics.Count ? lyrics[index + 1].Text : "";
    }
}
