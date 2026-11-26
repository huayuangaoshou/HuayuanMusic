using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using HuayuanPreview.R2;
using KugouAvaloniaPlayer.Services;
using Microsoft.Data.Sqlite;
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
        var line = JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, kind, detail }) + Environment.NewLine;
        try
        {
            lock (typeof(Program))
                File.AppendAllText(Path.Combine(Session, "events.jsonl"), line);
        }
        catch (IOException) { WriteCrashFallback(line); }
        catch (UnauthorizedAccessException) { WriteCrashFallback(line); }
    }

    /// <summary>
    /// Last-resort evidence sink. When %LOCALAPPDATA% is unwritable (permissions, disk full,
    /// read-only profile), a crash trace must still survive somewhere — so we append to a
    /// plain text file beside the executable. Never throws.
    /// </summary>
    private static void WriteCrashFallback(string line)
    {
        try
        {
            lock (typeof(Program))
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash-log.txt"), line);
        }
        catch { /* evidence sink is best-effort; do not let logging kill the app */ }
    }
}

public sealed class App : Application
{
    // Depth-1 guard: we quarantine a corrupt database and retry the full startup sequence
    // exactly once. If that retry still fails, we do NOT loop — we fall through to the native
    // error dialog so the app never silently dies.
    private static bool _dbQuarantined;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            BuildDesktop();
        }
        catch (Exception error)
        {
            TriageStartupFailure(error);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void BuildDesktop()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            throw new InvalidOperationException("Desktop lifetime required");

        // Pre-flight schema gate: force the database open + version check to run synchronously
        // BEFORE any window exists. A corrupt db (SqliteException) or a wrong user_version
        // (InvalidDataException) is caught here by TriageStartupFailure, quarantined, and the
        // whole startup is retried once — instead of surfacing later as a half-alive window.
        AppDatabase.EnsureDatabaseCreated();

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
        var tray = new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = "HuayuanMusic", Menu = trayMenu, IsVisible = true };
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

    /// <summary>
    /// Three-state startup triage. A corrupt database is quarantined and the startup retried
    /// exactly once; anything else (or a failed retry) goes straight to a native dialog.
    /// </summary>
    private void TriageStartupFailure(Exception error)
    {
        var root = Unwrap(error);

        if (root is SqliteException or InvalidDataException)
        {
            if (!_dbQuarantined)
            {
                _dbQuarantined = true;
                QuarantineDatabase(root);

                // One full restart of the startup sequence, depth-limited to prevent loops.
                try
                {
                    BuildDesktop();
                    Program.Log("STARTUP_RECOVERED", new { type = root.GetType().Name });
                    return;
                }
                catch (Exception retry)
                {
                    error = retry;
                }
            }
        }

        Program.Log("DESKTOP_STARTUP_FAILED", new { type = root.GetType().Name, summary = root.Message, error.StackTrace });
        Program.Result = 2;
        ShowFatalMessageBox(error);
    }

    private static Exception Unwrap(Exception error)
    {
        // Walk the inner-exception chain (and peel AggregateException) until we hit a
        // recognisable root cause, or the deepest exception available.
        var current = error;
        while (true)
        {
            if (current is AggregateException aggregate && aggregate.InnerException is { } aggregateInner)
            {
                current = aggregateInner;
                continue;
            }
            if (current is SqliteException or InvalidDataException)
                return current;
            if (current.InnerException is { } next)
            {
                current = next;
                continue;
            }
            return current;
        }
    }

    /// <summary>
    /// Rename the corrupt database (and its -wal/-shm sidecars) to a timestamped .corrupt name.
    /// The file is preserved for forensics — never deleted. Only src-side filesystem moves.
    /// </summary>
    private static void QuarantineDatabase(Exception root)
    {
        // The schema gate opened the database via Microsoft.Data.Sqlite, which pools connections
        // by default and may still hold the file handle. Clear the pool (and let the finalizer
        // release any remaining handle) before touching the files, then retry briefly.
        try { SqliteConnection.ClearAllPools(); } catch { }
        GC.Collect();
        GC.WaitForPendingFinalizers();

        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        foreach (var path in new[]
                 {
                     AppDatabase.DatabasePath,
                     AppDatabase.DatabasePath + "-wal",
                     AppDatabase.DatabasePath + "-shm",
                 })
        {
            if (!File.Exists(path))
                continue;
            // Retry a few times: on Windows the handle release can lag the pool clear by a tick.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(path, path + ".corrupt-" + stamp);
                    break;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(100 * (attempt + 1));
                }
                catch (Exception moveError)
                {
                    Program.Log("STARTUP_DB_QUARANTINE_PARTIAL", new { path, type = moveError.GetType().Name });
                    break;
                }
            }
        }
        Program.Log("STARTUP_DB_QUARANTINED", new
        {
            database = AppDatabase.DatabasePath,
            renamedTo = ".corrupt-" + stamp,
            exception = root.GetType().Name,
            summary = root.Message,
        });
    }

    private void ShowFatalMessageBox(Exception error)
    {
        var root = Unwrap(error);
        var logPath = Path.Combine(Program.Session, "events.jsonl");
        var message =
            $"类型：{root.GetType().Name}\n" +
            $"摘要：{root.Message}\n\n" +
            $"日志：{logPath}\n\n" +
            $"点击「是」打开日志文件夹，点击「否」退出。";

        // Native user32 MessageBoxW — must not depend on Avalonia, which may itself be the
        // reason startup failed.
        var choice = MessageBoxW(IntPtr.Zero, message, "化缘音乐无法启动",
            MB_YESNO | MB_ICONERROR | MB_SETFOREGROUND);

        if (choice == IDYES)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{logPath}\"")
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception) { /* non-fatal: the dialog already surfaced the path */ }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_YESNO = 0x00000004;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_SETFOREGROUND = 0x00010000;
    private const int IDYES = 6;
}
