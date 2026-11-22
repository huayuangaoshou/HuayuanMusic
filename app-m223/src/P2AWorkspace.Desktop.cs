using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HuayuanMusic.Provider.Kugou;
using KuGou.Net.Abstractions.Models;

namespace HuayuanPreview;

internal sealed partial class P2AWorkspace
{
    private CancellationTokenSource? desktopLogin;
    private Image? desktopQr;
    private Button? desktopLoginButton, desktopCancelButton, desktopRefreshButton, desktopDisconnectButton;

    private Control BuildDesktopAccountPage()
    {
        var actions = new WrapPanel();
        Button Add(string label, Action run)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) => run();
            actions.Children.Add(button);
            return button;
        }

        desktopLoginButton = Add("扫码登录", () => Pending = ConnectDesktopAsync(true));
        desktopCancelButton = Add("取消扫码", CancelDesktopOperations);
        desktopRefreshButton = Add("刷新歌单", () => Pending = RefreshAsync());
        desktopDisconnectButton = Add("断开账号", () => Pending = CloseDesktopConnectionAsync());
        desktopQr = new Image { Width = 220, Height = 220, IsVisible = false };
        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(12),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "酷狗概念版账号", FontSize = 21 },
                    accountStatus, actions, desktopQr
                }
            }
        };
    }

    private void UpdateDesktopAccountControls()
    {
        desktopLoginButton!.IsVisible = !Connected && !transitioning;
        desktopCancelButton!.IsVisible = desktopLogin != null;
        desktopRefreshButton!.IsVisible = Connected && !transitioning && read == null;
        desktopDisconnectButton!.IsVisible = Connected && !transitioning;
    }

    internal Task RestoreDesktopConnectionAsync() => Pending = ConnectDesktopAsync(false);

    private async Task ConnectDesktopAsync(bool allowQr)
    {
        if (transitioning || Connected)
            return;
        transitioning = true;
        using var login = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        desktopLogin = login;
        DirectSdkGateway? gateway = null;
        Bitmap? image = null;
        try
        {
            gateway = DirectSdkGateway.CreateDesktopSession();
            gateway.OneSongDiagnostic += phase => Program.Log("DESKTOP_SDK_AUDIO", new { phase });
            if (!allowQr && !gateway.LoggedIn)
                return;
            Report("正在连接酷狗", "desktop-connect");
            core.EnableKugou();
            await core.Kugou.ConnectSdkAsync(gateway, login.Token);
            gateway = null;
            if (!Connected)
            {
                var challenge = await core.Kugou.BeginQrAsync(login.Token);
                if (challenge.ImageBytes == null)
                    throw new InvalidDataException("酷狗未返回登录二维码");
                using var pixels = new MemoryStream(challenge.ImageBytes);
                image = new Bitmap(pixels);
                desktopQr!.Source = image;
                desktopQr.IsVisible = true;
                Report("请使用酷狗扫码登录", "desktop-qr");
                while (!Connected)
                {
                    var result = await core.Kugou.PollQrAsync(login.Token);
                    if (result == QrLoginStatus.Expired)
                        throw new TimeoutException("登录二维码已过期");
                    if (result == QrLoginStatus.Success)
                        break;
                    await Task.Delay(TimeSpan.FromSeconds(2), login.Token);
                }
                if (Connected)
                {
                    try { core.Kugou.SaveDirectLogin(); }
                    catch (Exception error)
                    {
                        Program.Log("DESKTOP_SAVE_LOGIN_FAILED", new { type = error.GetType().Name });
                    }
                }
            }
            if (!Connected)
                throw new UnauthorizedAccessException("酷狗未建立登录会话");
            if (AccountKey is { } key) Snapshot = PlatformPlaylistSnapshotStore.LoadLatest(key);
            Report("已登录", "desktop-connected");
        }
        catch (OperationCanceledException) { Report("登录已取消或超时", "desktop-cancel"); }
        catch (Exception error)
        {
            Program.Log("DESKTOP_CONNECT_FAILED", new { type = error.GetType().Name });
            Report("登录失败，可重新扫码", "desktop-connect-failed");
        }
        finally
        {
            gateway?.Dispose();
            desktopQr!.Source = null;
            desktopQr.IsVisible = false;
            image?.Dispose();
            desktopLogin = null;
            if (!Connected && core.Kugou.Enabled)
                await core.DisableKugouAsync();
            transitioning = false;
            UpdateDesktopAccountControls();
        }
        if (Connected && !login.IsCancellationRequested)
            await RefreshAsync();
    }

    internal void CancelDesktopOperations()
    {
        desktopLogin?.Cancel();
        read?.Cancel();
    }

    internal async Task CloseDesktopConnectionAsync()
    {
        CancelDesktopOperations();
        transitioning = true;
        try
        {
            core.Kugou.CancelPlaylistDetails();
            artwork.Revoke();
            if (core.Kugou.Enabled)
                await core.DisableKugouAsync();
            Snapshot = null;
        }
        finally
        {
            transitioning = false;
            Report("已断开", "desktop-disconnect");
        }
    }
}
