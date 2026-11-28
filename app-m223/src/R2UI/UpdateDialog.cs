using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HuayuanPreview.Services;

namespace HuayuanPreview.R2;

public partial class R2ShellView
{
    private ModalOverlay.Session<bool>? updateDialog;
    private UpdateManager.DownloadRoute updateRoute;

    /// <summary>
    /// Silent startup check: waits a few seconds after the main window opens so
    /// listening is never blocked, then only surfaces the dialog when a newer
    /// release is actually available.
    /// </summary>
    public void BeginSilentUpdateCheck()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            var result = await UpdateManager.CheckAsync();
            if (result.Release is { IsNewer: true } release)
                Dispatcher.UIThread.Post(() => OpenUpdateDialog(release));
        });
    }

    /// <summary>
    /// Manual "check for updates" entry from the settings page. Runs the latest
    /// release lookup, then either reports the friendly outcome or opens the
    /// pink-blue-white glass update dialog.
    /// </summary>
    private async void CheckForUpdates(object? sender, RoutedEventArgs args)
    {
        ShowToast("正在检查更新…");
        var result = await UpdateManager.CheckAsync();
        if (result.Release == null)
        {
            ShowToast(result.Message);
            return;
        }
        if (!result.Release.IsNewer)
        {
            ShowToast($"当前 v{UpdateManager.CurrentVersion} 已是最新版本");
            return;
        }
        OpenUpdateDialog(result.Release);
    }

    private async void OpenUpdateDialog(UpdateManager.ReleaseInfo release)
    {
        while (updateDialog == null && ModalOverlay.CurrentCompletion(this) is { } active)
            await active;
        if (!release.IsNewer || Window is not { } owner || updateDialog != null) return;

        var body = new SelectableTextBlock
        {
            Text = release.Body,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, DesignTokens.Space3, DesignTokens.Space2, DesignTokens.Space3),
            Classes = { "update-notes" }
        };
        var notes = new ScrollViewer
        {
            Content = body,
            Classes = { "update-scroll" },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowAutoHide = false,
            IsVisible = !string.IsNullOrWhiteSpace(release.Body)
        };

        var progress = new ProgressBar
        {
            Minimum = 0, Maximum = 1, Value = 0,
            IsVisible = false, Height = 8, CornerRadius = new CornerRadius(4)
        };

        var status = new TextBlock { FontSize = DesignTokens.FontXs, TextWrapping = TextWrapping.Wrap, IsVisible = false };

        var later = new Button { Content = "暂不", Classes = { "update-ghost" } };
        var update = new Button { Content = "去下载", Classes = { "update-primary" } };

        var routes = new ComboBox
        {
            ItemsSource = new[] { "自动", "直连 · GitHub", "镜像 · ghfast.top", "镜像 · gh-proxy.com", "镜像 · ghproxy.net" },
            SelectedIndex = (int)updateRoute,
            MinWidth = 176
        };
        AutomationProperties.SetName(routes, "更新下载线路");
        routes.SelectionChanged += (_, _) =>
        {
            if (routes.SelectedIndex >= 0)
                updateRoute = (UpdateManager.DownloadRoute)routes.SelectedIndex;
        };
        var copy = new Button { Content = "复制下载链接", Classes = { "update-ghost" }, IsVisible = false };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = DesignTokens.Space2,
            HorizontalAlignment = HorizontalAlignment.Right, Children = { later, copy, update }
        };

        var header = new StackPanel { Spacing = DesignTokens.Space1, Children =
        {
            new TextBlock { Text = $"发现新版本 v{release.Version}", Classes = { "section-title" }, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = $"当前版本 v{UpdateManager.CurrentVersion}", Classes = { "muted" }, FontSize = DesignTokens.FontXs }
        } };
        var footer = new StackPanel { Spacing = DesignTokens.Space2, Children =
        {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = DesignTokens.Space2, Children =
            {
                new TextBlock { Text = "线路", VerticalAlignment = VerticalAlignment.Center },
                routes
            } },
            progress, status, actions
        } };
        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = DesignTokens.Space4,
            Children = { header, notes, footer }
        };
        Grid.SetRow(notes, 1);
        Grid.SetRow(footer, 2);

        CancellationTokenSource? download = null;
        CancelSuggestions();
        var dialog = ModalOverlay.TryShow<bool>(this, content,
            new("软件更新", 520, InitialFocus: later, ShowTitle: false));
        if (dialog == null) return;
        updateDialog = dialog;
        dialog.Closed += () =>
        {
            download?.Cancel();
            updateDialog = null;
        };
        later.Click += (_, _) => dialog.Dismiss();

        copy.Click += async (_, _) =>
        {
            var manualLink = UpdateManager.GetManualDownloadUrl(release, updateRoute);
            if (manualLink == null || owner.Clipboard is not { } clipboard) return;
            try
            {
                await clipboard.SetTextAsync(manualLink);
                status.Text = "已复制，请粘贴到浏览器下载";
            }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
            {
                Program.Log("UPDATE_CLIPBOARD_FAILED", new { type = error.GetType().Name });
                status.Text = "无法复制链接，请前往发布页手动下载";
            }
        };

        update.Click += async (_, _) =>
        {
            if (download != null) return;
            status.IsVisible = true;
            var route = updateRoute;
            if (UpdateManager.GetManualDownloadUrl(release, route) == null)
            {
                status.Text = "未找到可用安装包，请前往发布页手动下载";
                return;
            }

            using var request = new CancellationTokenSource();
            download = request;
            string? installer = null;
            bool downloaded = false;
            update.IsEnabled = routes.IsEnabled = false;
            later.Content = "取消下载";
            later.Focus();
            copy.IsVisible = false;
            progress.IsVisible = progress.IsIndeterminate = true;
            progress.Value = 0;
            status.Text = "正在下载安装包…";
            var reporter = new Progress<UpdateManager.DownloadProgress>(value =>
            {
                if (!ReferenceEquals(download, request) || downloaded || request.IsCancellationRequested) return;
                progress.IsIndeterminate = value.Fraction == null;
                progress.Value = value.Fraction ?? 0;
                status.Text = value.Stage switch
                {
                    UpdateManager.DownloadStage.Verifying => "正在校验文件完整性…",
                    _ => value.Source is { } source ? $"正在通过 {source} 下载安装包…" : "正在下载安装包…"
                };
            });
            try
            {
                installer = await UpdateManager.DownloadInstallerAsync(release, reporter, request.Token, route);
                downloaded = true;
                status.Text = "下载完成，即将安装…";
                await Task.Delay(400, request.Token);
                LaunchInstaller(installer);
                installer = null;
                dialog.Complete(true);
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.TryShutdown();
                else Window?.Close();
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception error)
            {
                Program.Log("UPDATE_DOWNLOAD_FAILED", new { type = error.GetType().Name });
                if (updateDialog != dialog) return;
                status.Text = "下载或安装未完成，请重试，或前往发布页手动下载";
                progress.IsVisible = false;
                update.Content = "重试";
                update.IsEnabled = routes.IsEnabled = true;
                later.Content = "暂不";
                copy.IsVisible = true;
            }
            finally
            {
                download = null;
                if (installer != null)
                {
                    try { File.Delete(installer); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { Program.Log("UPDATE_CLEANUP_FAILED", new { type = error.GetType().Name }); }
                }
            }
        };

    }

    private static void LaunchInstaller(string installer)
    {
        var info = new ProcessStartInfo(installer)
        {
            Arguments = "/SILENT /NORESTART /CLOSEAPPLICATIONS",
            UseShellExecute = true
        };
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Installer did not start.");
        Program.Log("UPDATE_INSTALLER_LAUNCHED", new { verified = true });
    }
}
