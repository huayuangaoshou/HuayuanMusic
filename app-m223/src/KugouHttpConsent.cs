using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace HuayuanPreview;

public sealed partial class PreviewWindow
{
    internal Task<bool> ConfirmKugouProbeImportAsync()
    {
        var tcs=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async()=>{
            try
            {
                if(!IsVisible||!IsEnabled){tcs.TrySetResult(false);return;}
                var title=new TextBlock {
                    Text="从你此前成功试听的 HuayuanMusic R2 实验导入登录",
                    FontSize=17,FontWeight=FontWeight.SemiBold,
                    TextWrapping=TextWrapping.Wrap
                };
                var notice=new TextBlock {
                    Text="你此前主动保存在 Windows 凭据管理器中的 HuayuanMusic 实验登录，将被一次性复制到本候选的独立登录槽。\n\n"+
                         "不会读取 KA Music、酷狗官方客户端或其他软件的凭据；不会把账号 Token 写进项目文件，也不会执行网络请求。\n\n"+
                         "导入后本软件可能以你当前账号访问酷狗，关闭窗口仍可使用已保存登录。仅在你确认是自己的账号时继续。",
                    TextWrapping=TextWrapping.Wrap
                };
                var yes=new Button{Content="确认一次性导入",MinWidth=145};
                var no=new Button{Content="取消",MinWidth=85};
                var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12,HorizontalAlignment=HorizontalAlignment.Right};
                actions.Children.Add(no);actions.Children.Add(yes);
                var body=new StackPanel{Margin=new Thickness(20),Spacing=16};
                body.Children.Add(title);body.Children.Add(notice);body.Children.Add(actions);
                var popup=new Window {
                    Title="HuayuanMusic 登录迁移确认",
                    Width=525,Height=325,CanResize=false,
                    WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=body
                };
                yes.Click+=(_,_)=>popup.Close(true);
                no.Click+=(_,_)=>popup.Close(false);
                tcs.TrySetResult(await popup.ShowDialog<bool>(this));
            }
            catch{tcs.TrySetResult(false);}
        });
        return tcs.Task;
    }

    int kugouHttpSessionAllowed;
    internal bool IsKugouHttpSessionApproved=>Volatile.Read(ref kugouHttpSessionAllowed)==1;
    internal void RevokeKugouHttpSession()=>Volatile.Write(ref kugouHttpSessionAllowed,0);

    internal async Task<bool> ApproveKugouHttpSessionAsync()
    {
        return await ShowKugouRiskAsync() && IsKugouHttpSessionApproved;
    }

    static bool TrustedKugouMediaHost(string host)
    {
        if(string.IsNullOrWhiteSpace(host)||Uri.CheckHostName(host)!=UriHostNameType.Dns)
            return false;
        return new[]{"kugou.com","kugoucdn.com","kgimg.com","kgcdn.com","kugou.net"}
            .Any(root=>host.Equals(root,StringComparison.OrdinalIgnoreCase)||
                host.EndsWith("."+root,StringComparison.OrdinalIgnoreCase));
    }

    // Called by the SAME existing provider's audio-open path, never during background
    // login, search, VIP checks or program startup. No signed URI is shown or logged.
    internal Task<bool> ConfirmKugouHttpAsync(string host)
    {
        if(!TrustedKugouMediaHost(host))return Task.FromResult(false);
        // No per-song prompts: absent explicit session choice means strict HTTPS-only.
        return Task.FromResult(IsKugouHttpSessionApproved);
    }
}
