using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HuayuanPreview.Services;

namespace HuayuanPreview.R2;

public partial class R2ShellView
{
    private Window? updateDialog;

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

    private void OpenUpdateDialog(UpdateManager.ReleaseInfo release)
    {
        if (!release.IsNewer || Window is not { } owner || updateDialog != null) return;

        var body = new TextBlock
        {
            Text = release.Body,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            LineHeight = 15 * DesignTokens.LineHeightBody
        };
        var notes = new ScrollViewer
        {
            Content = body,
            MaxHeight = 192,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsVisible = !string.IsNullOrWhiteSpace(release.Body)
        };

        var progress = new ProgressBar
        {
            Minimum = 0, Maximum = 1, Value = 0,
            IsVisible = false, Height = 8, CornerRadius = new CornerRadius(4)
        };

        var status = new TextBlock { FontSize = DesignTokens.FontXs, TextWrapping = TextWrapping.Wrap, IsVisible = false };

        var later = new Button { Content = "暂不", Classes = { "update-ghost" }, IsCancel = true };
        var update = new Button { Content = "去下载", Classes = { "update-primary" } };

        var manualLink = UpdateManager.GetManualDownloadUrl(release);
        var copy = new Button { Content = "复制下载链接", Classes = { "update-ghost" }, IsVisible = false };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = DesignTokens.Space2,
            HorizontalAlignment = HorizontalAlignment.Right, Children = { later, copy, update }
        };

        var content = new StackPanel { Margin = new Thickness(DesignTokens.Space5), Spacing = DesignTokens.Space4, Children =
        {
            new StackPanel { Spacing = DesignTokens.Space1, Children =
            {
                new TextBlock { Text = $"发现新版本 v{release.Version}", FontSize = DesignTokens.BlockTitleSize, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = $"当前版本 v{UpdateManager.CurrentVersion}", FontSize = DesignTokens.FontXs }
            } },
            notes,
            progress,
            status,
            actions
        } };

        var panel = new Border
        {
            Classes = { "glass", "update-panel" },
            Margin = new Thickness(DesignTokens.Space3),
            Child = new Grid
            {
                Children =
                {
                    new GlassOptics { Margin = new Thickness(1), Radius = DesignTokens.RadiusM - 1, Frost = DesignTokens.FrostRegular, Saturation = DesignTokens.SaturationChrome, IsHitTestVisible = false },
                    new Border { Classes = { "glass-edge" } },
                    content
                }
            }
        };
        var dialog = new Window
        {
            Classes = { "update-dialog" },
            Title = "软件更新",
            Width = 440, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            RequestedThemeVariant = ActualThemeVariant,
            Content = panel
        };
        if (!DesignTokens.ShaderDegrade)
            panel.Classes.Add("glass-enter");

        CancellationTokenSource? download = null;
        updateDialog = dialog;
        dialog.Closed += (_, _) =>
        {
            download?.Cancel();
            updateDialog = null;
        };

        later.Click += (_, _) => dialog.Close();

        copy.Click += async (_, _) =>
        {
            if (manualLink == null || dialog.Clipboard is not { } clipboard) return;
            try
            {
                await clipboard.SetTextAsync(manualLink);
                ShowToast("已复制，请粘贴到浏览器下载");
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
            if (manualLink == null)
            {
                status.Text = "未找到可用安装包，请前往发布页手动下载";
                return;
            }

            using var request = new CancellationTokenSource();
            download = request;
            string? installer = null;
            bool downloaded = false;
            update.IsEnabled = later.IsEnabled = false;
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
                    UpdateManager.DownloadStage.Mirror => "直连较慢，正在通过加速镜像下载…",
                    UpdateManager.DownloadStage.Verifying => "正在校验文件完整性…",
                    _ => "正在下载安装包…"
                };
            });
            try
            {
                installer = await UpdateManager.DownloadInstallerAsync(release, reporter, request.Token);
                downloaded = true;
                status.Text = "下载完成，即将安装…";
                await Task.Delay(400, request.Token);
                LaunchInstaller(installer);
                installer = null;
                dialog.Close();
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.TryShutdown();
                else Window?.Close();
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception error)
            {
                Program.Log("UPDATE_DOWNLOAD_FAILED", new { type = error.GetType().Name });
                if (!dialog.IsVisible) return;
                status.Text = "下载或安装未完成，请重试，或前往发布页手动下载";
                progress.IsVisible = false;
                update.Content = "重试";
                update.IsEnabled = later.IsEnabled = true;
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

        dialog.ShowDialog(owner);
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

internal static class GlassDialogSurface
{
    internal static Border Create(Control content, Thickness padding)
    {
        var border = new Border
        {
            Classes = { "glass", "glass-float" },
            Margin = new Thickness(DesignTokens.Space3),
            Child = new Grid
            {
                Children =
                {
                    new Border { Classes = { "glass-edge" } },
                    new Border { Padding = padding, Child = content }
                }
            }
        };
        if (!DesignTokens.ShaderDegrade) border.Classes.Add("glass-enter");
        return border;
    }
}
