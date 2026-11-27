using Avalonia.Controls;
using Avalonia.Threading;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.GlobalShortcutService;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace HuayuanPreview.R2;

internal sealed class DesktopGlobalShortcuts : IDisposable
{
    private readonly GlobalShortcutService service;
    private readonly CorePresentation player;
    private bool disposed;

    internal DesktopGlobalShortcuts(PlayerViewModel core, R2ShellView view, CorePresentation player)
    {
        this.player = player;
        var dispatcher = new UiDispatcherService(Dispatcher.UIThread);
        service = new GlobalShortcutService(core, new LyricsShortcut(view), new MainWindowService(dispatcher),
            dispatcher, NullLogger<GlobalShortcutService>.Instance);
    }

    internal void Initialize(Window window, GlobalShortcutSettings settings)
    {
        try
        {
            service.Initialize(window);
            service.RegistrationChanged -= Report;
            service.RegistrationChanged += Report;
            service.LoadFromSettings(settings);
        }
        catch (Exception error)
        {
            Program.Log("DESKTOP_SHORTCUTS_FAILED", new { type = error.GetType().Name });
            player.Notify("全局快捷键暂不可用");
        }
    }

    private void Report(IReadOnlyDictionary<GlobalShortcutAction, GlobalShortcutRegistrationResult> results)
    {
        Program.Log("DESKTOP_SHORTCUTS_REGISTRATION", new
        {
            actions = results.Select(entry => new
            {
                action = entry.Key.ToString(), registered = entry.Value.IsRegistered,
                failure = entry.Value.FailureKind.ToString()
            }).ToArray()
        });
        if (results.Values.Any(result => result.FailureKind == GlobalShortcutRegistrationFailureKind.Conflict))
            player.Notify("部分全局快捷键已被系统或其他应用占用");
        else if (results.Values.Any(result => result.FailureKind is
                     GlobalShortcutRegistrationFailureKind.InvalidGesture or GlobalShortcutRegistrationFailureKind.PlatformError))
            player.Notify("部分全局快捷键无法注册");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        service.RegistrationChanged -= Report;
        try { service.UnregisterAll(); }
        catch (Exception error) { Program.Log("DESKTOP_SHORTCUTS_FAILED", new { type = error.GetType().Name }); }
    }

    private sealed class LyricsShortcut(R2ShellView view) : IDesktopLyricWindowService
    {
        public bool IsOpen => view.DesktopLyricsVisible;
        public event Action<bool>? IsOpenChanged
        {
            add => view.DesktopLyricsVisibilityChanged += value;
            remove => view.DesktopLyricsVisibilityChanged -= value;
        }
        public void Toggle() => view.ToggleDesktopLyricsFromHost();
        public void Close() { if (IsOpen) Toggle(); }
        public void Unlock() => view.UnlockDesktopLyricsFromHost();
        public void RefreshBackend() { }
    }
}
