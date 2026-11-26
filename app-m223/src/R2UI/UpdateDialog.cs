using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = DesignTokens.Space2, HorizontalAlignment = HorizontalAlignment.Right, Children = { later, update } };

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
            panel.Classes.Add("update-enter");

        updateDialog = dialog;
        dialog.Closed += (_, _) => updateDialog = null;

        later.Click += (_, _) => dialog.Close();

        update.Click += async (_, _) =>
        {
            status.IsVisible = true;
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
