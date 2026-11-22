using Avalonia.Controls;
namespace HuayuanPreview.R2;
public partial class R2ShellView:UserControl {public R2ShellView(){InitializeComponent();if(Program.Desktop)this.FindControl<TextBlock>("EnvironmentNotice")!.IsVisible=false;else if(P2AWorkspace.RealReadOnly)this.FindControl<TextBlock>("EnvironmentNotice")!.Text=P2AWorkspace.RealEntitlementOnly?"酷狗概念版：VIP 状态一次性只读查询 · 无领会员、音源、写回":P2AWorkspace.RealCombinedVipAudio?"酷狗概念版：一次会员GET+一首6秒短试听 · 不领取、不写回":P2AWorkspace.RealSingleAudio?"本地使用隔离 WAV · 酷狗概念版：一次性单曲短试听 · 不领 VIP、不写回":"本地音乐使用隔离 WAV · 酷狗概念版：真实账号 HTTPS 只读 · 不播放、不写回、不领 VIP";}}
