using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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

    [STAThread]
    public static int Main(string[] args)
    {
        using var instance = new Mutex(true, "Local\\HuayuanMusic.M223.Desktop", out bool primary);
        if (!primary)
            return 0;

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
        SimpleAudioPlayer.Initialize(SettingsManager.Settings.AudioOutputDeviceId);
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

        window.Opened += (_, _) =>
        {
            initialization = InitializeAsync();
            window.R2View?.RestoreDesktopLyrics();
        };
        window.Closing += async (_, args) =>
        {
            if (closed)
                return;
            args.Cancel = true;
            if (closing)
                return;
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
                if (!closing)
                    await window.R2Model!.Concept.RestoreDesktopConnectionAsync();
            }
            catch (Exception error)
            {
                Program.Log("DESKTOP_INITIALIZATION_FAILED", new { type = error.GetType().Name, error.StackTrace });
                Program.Result = 2;
            }
        }
    }
}
