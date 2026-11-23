using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
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
        if (Window is not { } owner || updateDialog != null) return;

        var body = new TextBlock
        {
            Text = release.Body,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 220,
            Opacity = 0.78
        };

        var progress = new ProgressBar
        {
            Minimum = 0, Maximum = 1, Value = 0,
            IsVisible = false, Height = 8, CornerRadius = new CornerRadius(4)
        };

        var status = new TextBlock { Classes = { "muted" }, Text = " ", FontSize = 12 };

        var later = new Button { Content = "稍后再说", Classes = { "glass-pill" } };
        var update = new Button { Content = "立即更新", Classes = { "glass-pill", "primary" } };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { later, update } };

        var content = new StackPanel { Spacing = 16, Children =
        {
            new StackPanel { Spacing = 6, Children =
            {
                new TextBlock { Text = $"发现新版本 v{release.Version}", FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = $"当前版本 v{UpdateManager.CurrentVersion} · {release.Name}", Classes = { "muted" }, FontSize = 13 }
            } },
            body,
            progress,
            status,
            actions
        } };

        var dialog = new Window
        {
            Title = "软件更新",
            Width = 440, Height = 360, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            RequestedThemeVariant = ActualThemeVariant,
            Content = new Border
            {
                Classes = { "glass", "glass-float" },
                Margin = new Thickness(12),
                Padding = new Thickness(8),
                Child = new StackPanel
                {
                    Children =
                    {
                        new GlassOptics { Radius = 22, Frost = 0.88, Margin = new Thickness(1), IsHitTestVisible = false },
                        content
                    }
                }
            }
        };

        updateDialog = dialog;
        dialog.Closed += (_, _) => updateDialog = null;

        later.Click += (_, _) => dialog.Close();

        update.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(release.SetupUrl))
            {
                status.Text = "未找到可用安装包，请前往发布页手动下载";
                return;
            }
            update.IsEnabled = false;
            later.IsEnabled = false;
            progress.IsVisible = true;
            status.Text = "正在下载安装包…";
            var reporter = new Progress<double>(value =>
                Dispatcher.UIThread.Post(() =>
                {
                    progress.Value = value;
                    status.Text = $"正在下载 {value * 100:0}%";
                }));
            try
            {
                var installer = await UpdateManager.DownloadInstallerAsync(release.SetupUrl, reporter);
                status.Text = "下载完成，即将安装…";
                await Task.Delay(400);
                dialog.Close();
                LaunchInstallerAndExit(installer);
            }
            catch (Exception error)
            {
                Program.Log("UPDATE_DOWNLOAD_FAILED", new { type = error.GetType().Name });
                status.Text = "下载失败，请稍后重试";
                progress.IsVisible = false;
                update.IsEnabled = true;
                later.IsEnabled = true;
            }
        };

        dialog.ShowDialog(owner);
    }

    /// <summary>
    /// Launches the downloaded installer with silent-upgrade flags, then asks the
    /// main window to close so the old binary can be replaced.
    /// </summary>
    private void LaunchInstallerAndExit(string installer)
    {
        try
        {
            var info = new ProcessStartInfo(installer)
            {
                Arguments = "/SILENT /NORESTART /CLOSEAPPLICATIONS",
                UseShellExecute = true
            };
            Process.Start(info);
            Program.Log("UPDATE_INSTALLER_LAUNCHED", new { path = installer });
        }
        catch (Exception error)
        {
            Program.Log("UPDATE_INSTALLER_LAUNCH_FAILED", new { type = error.GetType().Name });
        }
        // Graceful shutdown: this triggers the normal Closing pipeline in Program.cs.
        Window?.Close();
    }
}
