using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using HuayuanPreview.R2;
using KugouAvaloniaPlayer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SimpleAudio;

namespace HuayuanPreview;

internal static class Program
{
    internal static bool Desktop => true;
    public static string Root = AppContext.BaseDirectory;
    public static string Session = "";
    public static string[] Args = [];
    public static int Result;
    internal static EventWaitHandle? Activation { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        using var instance = new Mutex(true, "Local\\HuayuanMusic.M223.Desktop", out bool primary);
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\HuayuanMusic.M223.Wake");
        if (!primary)
        {
            activation.Set();
            return 0;
        }
        Activation = activation;

        Args = args;
        Session = Path.Combine(AppDataPaths.LocalRoot, "logs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Session);
        try
        {
            AppBuilder.Configure<App>().UsePlatformDetect()
                .StartWithClassicDesktopLifetime([], ShutdownMode.OnMainWindowClose);
        }
        catch (Exception error)
        {
            Log("DESKTOP_STARTUP_FAILED", new { type = error.GetType().Name, error.StackTrace });
            Result = 2;
        }
        return Result;
    }

    public static void Log(string kind, object detail)
    {
        try
        {
            lock (typeof(Program))
                File.AppendAllText(Path.Combine(Session, "events.jsonl"),
                    JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, kind, detail }) + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            throw new InvalidOperationException("Desktop lifetime required");

        SettingsManager.Load();
        SimpleAudioPlayer.DesktopMicroFade = DesktopPreferences.Behavior.MicroFade;
        var outputFailed=false;
        try { SimpleAudioPlayer.Initialize(SettingsManager.Settings.AudioOutputDeviceId); }
        catch(Exception error) when (error is not OutOfMemoryException)
        {
            outputFailed=true;
            Program.Log("DESKTOP_OUTPUT_INIT_FAILED",new {type=error.GetType().Name});
        }
        var composition = new AvaloniaAppServiceProvider();
        var owned = composition.CreateOfflineWindowRoot(NullLoggerFactory.Instance, Dispatcher.UIThread);
        var root = owned.Value;
        var adapter = new CorePlaybackAdapter(root);
        var window = new PreviewWindow(adapter);
        window.MountDesktopR2(adapter);
        desktop.MainWindow = window;
        Task initialization = Task.CompletedTask;
        bool closing = false;
        bool closed = false;
        bool exitRequested = false;
        var shell = window.R2Model!;
        var view = window.R2View!;
        using var icon = AssetLoader.Open(new Uri("avares://HuayuanMusic.P17.Nightflight/Assets/app.ico"));
        var trayMenu = new NativeMenu();
        NativeMenuItem AddTray(string label, Action action)
        {
            var item = new NativeMenuItem(label);
            item.Click += (_, _) => action();
            trayMenu.Add(item);
            return item;
        }
        var playPause = AddTray("播放", () => shell.Player.TogglePlayPauseCommand.Execute(null));
        AddTray("上一首", () => shell.Player.PlayPreviousCommand.Execute(null));
        AddTray("下一首", () => shell.Player.PlayNextCommand.Execute(null));
        AddTray("显示 / 隐藏桌面歌词", view.ToggleDesktopLyricsFromHost);
        AddTray("解锁桌面歌词", view.UnlockDesktopLyricsFromHost);
        AddTray("打开主窗口", Reveal);
        AddTray("彻底退出", () => { exitRequested = true; window.Close(); });
        var tray = new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = "HuayuanMusic · 花苑音乐", Menu = trayMenu, IsVisible = true };
        tray.Clicked += (_, _) => Reveal();
        shell.Player.Updated += UpdateTray;
        var activationRegistration = Program.Activation == null ? null :
            ThreadPool.RegisterWaitForSingleObject(Program.Activation, (_, _) =>
                Dispatcher.UIThread.Post(() => { if (!closing) { Reveal(); Program.Log("DESKTOP_SINGLE_INSTANCE_WAKE", new { raised = true }); } }),
                null, Timeout.Infinite, false);

        void UpdateTray() => playPause.Header = shell.Player.IsPlayingAudio ? "暂停" : "播放";
        void Reveal()
        {
            if (!window.IsVisible) window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        window.Opened += (_, _) =>
        {
            root.SystemMediaSessionService.Initialize(window, root.Player);
            if (root.SystemMediaSessionService is KugouAvaloniaPlayer.Services.SystemMediaSession.SystemMediaSessionService media)
                Program.Log("DESKTOP_SMTC", new { active = media.HasTransportControls });
            initialization = InitializeAsync();
            if(outputFailed)shell.Player.Notify("无法播放，请检查音频输出设备");
            window.R2View?.RestoreDesktopLyrics();
            window.R2View?.BeginSilentUpdateCheck();
        };
        window.Closing += async (_, args) =>
        {
            if (closed)
                return;
            args.Cancel = true;
            if (closing)
                return;
            if (!exitRequested && args.CloseReason == WindowCloseReason.WindowClosing && DesktopPreferences.Behavior.TrayOnClose)
            {
                window.Hide();
                Program.Log("DESKTOP_TRAY_HIDE", new { playing = shell.Player.IsPlayingAudio });
                return;
            }
            closing = true;
            window.IsEnabled = false;
            try
            {
                window.R2Model!.Concept.CancelDesktopOperations();
                if (window.R2View != null) await window.R2View.CloseDesktopLyricsForExitAsync();
                await window.CancelImportAndWait();
                await initialization;
                await window.R2Model.Concept.Pending;
                await window.R2Model.Player.Pending;
                await window.R2Model.LocalWorkspace.PendingFolderWork;
                await shell.DlcPending;
                adapter.DesktopQueueTracking=false;
                await adapter.SaveDesktopQueueAsync();
                await window.R2Model.Concept.CloseDesktopConnectionAsync();
            }
            catch (Exception error)
            {
                Program.Log("DESKTOP_CLOSE_ERROR", new { type = error.GetType().Name });
                Program.Result = 2;
            }
            finally
            {
                closed = true;
                window.Close();
            }
        };
        desktop.Exit += (_, _) =>
        {
            activationRegistration?.Unregister(null);
            shell.Player.Updated -= UpdateTray;
            tray.Dispose();
            try
            {
                adapter.Dispose();
                root.ViewModel.Dispose();
                owned.Dispose();
                composition.Dispose();
            }
            finally
            {
                SimpleAudioPlayer.Free();
            }
        };
        base.OnFrameworkInitializationCompleted();

        async Task InitializeAsync()
        {
            try
            {
                await adapter.InitializeAsync();
                Program.Log("DESKTOP_R2_READY", new { singleCore = true, tracks = adapter.State.Tracks.Length });
                if (!closing && shell.KugouEnabled)
                    await window.R2Model!.Concept.RestoreDesktopConnectionAsync();
                if (!closing)
                {
                    await adapter.RestoreDesktopQueueAsync(shell.Concept);
                    adapter.StartDesktopQueueTracking();
                }
            }
            catch (Exception error)
            {
                Program.Log("DESKTOP_INITIALIZATION_FAILED", new { type = error.GetType().Name, error.StackTrace });
                Program.Result = 2;
            }
        }
    }
}
