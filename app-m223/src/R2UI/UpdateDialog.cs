using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HuayuanPreview.Services;

namespace HuayuanPreview.R2;

public partial class R2ShellView
{
    private Grid? updateDialog;
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

    private void OpenUpdateDialog(UpdateManager.ReleaseInfo release)
    {
        if (!release.IsNewer || Window is not { } owner || updateDialog != null ||
            OverlayLayer.GetOverlayLayer(this) is not { } layer) return;

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
            Margin = new Thickness(DesignTokens.Space5),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = DesignTokens.Space4,
            Children = { header, notes, footer }
        };
        Grid.SetRow(notes, 1);
        Grid.SetRow(footer, 2);

        var panel = new Border
        {
            Classes = { "glass", "glass-float" },
            BorderThickness = new Thickness(0),
            Margin = new Thickness(DesignTokens.Space5),
            MaxWidth = 520,
            MaxHeight = 600,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = true,
            Child = new Grid
            {
                Children =
                {
                    new GlassOptics { Radius = DesignTokens.RadiusM, Frost = DesignTokens.FrostRegular, Saturation = DesignTokens.SaturationChrome, IsHitTestVisible = false },
                    content
                }
            }
        };
        var dialog = new Grid
        {
            Classes = { "update-overlay" },
            Children = { panel }
        };
        dialog.Bind(WidthProperty, new Binding("ClientSize.Width") { Source = owner });
        dialog.Bind(HeightProperty, new Binding("ClientSize.Height") { Source = owner });
        AutomationProperties.SetName(panel, "软件更新");
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
        if (!DesignTokens.ShaderDegrade)
            panel.Classes.Add("glass-enter");

        CancellationTokenSource? download = null;
        var previousFocus = owner.FocusManager?.GetFocusedElement();
        CancelSuggestions();
        updateDialog = dialog;
        DetachedFromVisualTree += CloseOnDetach;
        void CloseOnDetach(object? sender, VisualTreeAttachmentEventArgs args) => CloseOverlay();
        void CloseOverlay()
        {
            if (updateDialog != dialog) return;
            DetachedFromVisualTree -= CloseOnDetach;
            download?.Cancel();
            updateDialog = null;
            layer.Children.Remove(dialog);
            previousFocus?.Focus();
        }

        dialog.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            CloseOverlay();
            args.Handled = true;
        };
        later.Click += (_, _) => CloseOverlay();

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
                    UpdateManager.DownloadStage.Mirror => route == UpdateManager.DownloadRoute.Automatic
                        ? "已切换到镜像，正在下载安装包…"
                        : "正在通过所选镜像下载安装包…",
                    UpdateManager.DownloadStage.Verifying => "正在校验文件完整性…",
                    _ => "正在下载安装包…"
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
                CloseOverlay();
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

        layer.Children.Add(dialog);
        later.Focus();
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
