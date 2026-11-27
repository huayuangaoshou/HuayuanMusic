using Avalonia.LogicalTree;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using HuayuanMusic.Provider.Kugou;
using KuGou.Net.Abstractions.Models;
namespace HuayuanPreview;

internal sealed partial class CorePlaybackAdapter {
 // Only the trusted composition root may supply this AFTER platform/operation approval.
 // No file/env/CLI switch turns arbitrary strings into an authorized identity.
 internal Func<IntegratedSdkGateway>? ApprovedKugouConnection{get;set;}
 internal Func<DirectSdkGateway>? DirectKugouConnection{get;set;}
 internal async Task ConnectDirectKugouAsync(){
  if(Kugou.SdkConnected)return;
  if(KugouAvaloniaPlayer.Services.Startup.OfflineGuiApplication.SelfTest)throw new UnauthorizedAccessException("离线测试不允许真实网关");
  var factory=DirectKugouConnection??throw new UnauthorizedAccessException("成熟 SDK 未配置");
  var next=factory();try{EnableKugou();await Kugou.ConnectSdkAsync(next);}catch{next.Dispose();throw;}
 }
 internal async Task ConnectApprovedKugouAsync(){
  if(Kugou.SdkConnected)return;
  var factory=ApprovedKugouConnection??throw new UnauthorizedAccessException("尚无获准的身份、设备和签名配置；没有发送请求");
  var next=factory();try{EnableKugou();await Kugou.ConnectSdkAsync(next);}catch{next.Dispose();throw;}
 }
}
public sealed partial class PreviewWindow {
 CancellationTokenSource? kugouQrCancel;Bitmap? kugouQrBitmap;Image? kugouQrImage;
 TextBlock? kugouAccountState,kugouVipState;bool kugouLoginRunning,kugouUiSubscribed;string kugouVipSummary="权益：未查询。领取成功、权益生效、歌曲获准及实际播放分别判断。";
 string M206AccountLabel()=>core is CorePlaybackAdapter a&&a.Kugou.LoggedIn?" · 账号本机标识 "+(a.Kugou.DirectAccountCacheKey?[..8]??"已连接"):"";
 internal TimeSpan KugouQrDeadline=TimeSpan.FromMinutes(2);
 void KugouUiRevoked(){var duringLogin=kugouLoginRunning;Avalonia.Threading.Dispatcher.UIThread.Post(()=>{var active=core as CorePlaybackAdapter;bool attached=active?.Kugou.SdkConnected==true;ReconcileKugouConsent();if(active?.Kugou.LoggedIn!=true)foreach(var button in kugouPlaylistPlayButtons)button.IsEnabled=false;kugouVipSummary=attached?"权益：未查询（当前连接已建立）":"权益：未查询（授权已撤销）";if(kugouVipState!=null)kugouVipState.Text=kugouVipSummary;if(kugouAccountState!=null&&!duringLogin)kugouAccountState.Text=attached?(active!.Kugou.LoggedIn?"已恢复登录；请主动查询权益":"连接已准备；请扫码登录"):"授权已撤销或尚未登录";});}
 internal void CancelKugouLogin(){kugouQrCancel?.Cancel();if(!kugouLoginRunning){ClearKugouQr();if(kugouAccountState!=null)kugouAccountState.Text="二维码显示已关闭；未改变账号状态";}}
 void ShowKugouQrPreview(){if(kugouLoginRunning||kugouBusy)return;ClearKugouQr();using var input=Avalonia.Platform.AssetLoader.Open(new Uri("avares://HuayuanMusic.P17.Nightflight/Assets/offline-test-qr.png"));kugouQrBitmap=new Bitmap(input);kugouQrImage!.Source=kugouQrBitmap;kugouQrImage.IsVisible=true;kugouAccountState!.Text="离线二维码演练：不是登录码，不建立会话、不联网。可点击取消关闭。";}
 void ClearKugouQr(){if(kugouQrImage!=null){kugouQrImage.Source=null;kugouQrImage.IsVisible=false;}kugouQrBitmap?.Dispose();kugouQrBitmap=null;}
 Control? kugouPageCache;
 Control KugouPage(){
  if(kugouPageCache!=null)return kugouPageCache;
  if(core is not CorePlaybackAdapter a)return T("仅受控宿主可用",16);
  if(!kugouUiSubscribed){kugouUiSubscribed=true;a.Kugou.Revoked+=KugouUiRevoked;Closed+=(_,_)=>{a.Kugou.Revoked-=KugouUiRevoked;ClearKugouQr();RevokeKugouHttpSession();};}
  var fixture=KugouFixturePage();
  kugouAccountState=T(a.Kugou.SdkConnected?(a.Kugou.LoggedIn?"已登录；权益请主动查询":"连接已准备；尚未登录"):"尚未连接；请主动点击连接酷狗，默认不联网",13,true);
  kugouVipState=T(kugouVipSummary,12,true);
  kugouStatus=T("默认不登录、不领取、不播放。",12,true);kugouResults=V();kugouOfficialPlaylists=V();
  kugouQrImage=new Image{Width=220,Height=220,IsVisible=false};
  var keyword=new TextBox{PlaceholderText="搜索酷狗歌曲（需获准连接及登录）",Width=280};
  kugouPageCache=V(NH("KUGOU","酷狗概念版","登录后同步官方歌单，使用本体播放器播放。"),
   H(NB("连接酷狗",()=>KugouAction(async()=>{if(!await ApproveM205ConnectionAsync())return;await a.ConnectDirectKugouAsync();kugouAccountState.Text=a.Kugou.LoggedIn?"已恢复保存的登录；权益需主动查询"+M206AccountLabel():"连接已准备；请扫码登录";})),
    NB(KugouAvaloniaPlayer.Services.Startup.OfflineGuiApplication.SelfTest?"连接获准配置":"高级连接配置",()=>KugouAction(async()=>{if(!KugouAvaloniaPlayer.Services.Startup.OfflineGuiApplication.SelfTest&&!await ApproveM205ConnectionAsync())return;await a.ConnectApprovedKugouAsync();kugouAccountState.Text="连接已准备；未登录，不自动请求";})),
    NB("二维码登录",StartKugouQr),NB("取消登录",CancelKugouLogin),NB("离线二维码演练",ShowKugouQrPreview)),
   kugouAccountState,kugouQrImage,
   H(NB("导入我之前的 R2 登录（确认后）",()=>KugouAction(async()=>{if(!await ConfirmKugouProbeImportAsync())return;var imported=a.Kugou.ImportVerifiedProbeLogin();KugouFeedback(imported?"已从 HuayuanMusic R2 实验导入登录；未自动发起网络请求":"没有找到之前保存的实验登录；可自行扫码");if(imported&&a.Kugou.IsDirectSdk)kugouAccountState.Text="已导入并恢复自己的账号；请主动查询权益";})),
    NB("保存登录（Windows）",()=>KugouAction(async()=>{DemandSdk(a);a.Kugou.SaveDirectLogin();KugouRiskSettings.SavePreference(true);KugouFeedback("已保存本应用独立登录；下次连接成熟 SDK 可恢复");await Task.CompletedTask;})),
    NB("删除已保存登录",()=>KugouAction(async()=>{var result=await CredentialDeletion.RunAsync(()=>ConfirmM205Async("删除 Windows 已保存登录", "将删除共享 Windows 凭据槽 HuayuanMusic/Kugou/IntegratedAccount/v1。其他 HuayuanMusic 测试候选可能同时失去保存的登录。退出当前账号只撤销当前会话，不删除此槽；本次删除不退出当前账号，不删除历史歌单或本地音乐资料。确认继续删除？"),a.Kugou.DeleteStoredDirectLogin);KugouFeedback(result switch{CredentialDeletionResult.Cancelled=>"已取消；未调用 Windows 凭据删除",CredentialDeletionResult.Deleted=>"Windows 已保存登录已删除（或原本不存在）；其他候选的保存登录也可能受影响。当前账号会话、歌单和本地数据未改变。",_=>"删除 Windows 已保存登录失败；未确认删除成功，请重试。当前会话与本地数据未改变。"});}))),
   H(NB("查询权益",()=>KugouAction(async()=>{DemandSdk(a);var e=await a.Kugou.RefreshEntitlementAsync();kugouVipState.Text=kugouVipSummary=$"ENTITLEMENT_ACTIVE={e.Active} · {e.Product} · {e.Until}";})),
    NB("查询领取状态",()=>KugouAction(async()=>{DemandSdk(a);var h=await a.Kugou.ReadClaimHistoryAsync();if(h.ServerTime<=0)throw new InvalidDataException("服务端日期不可用");var day=DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(h.ServerTime).ToOffset(TimeSpan.FromHours(8)).DateTime);bool received=h.Items.Any(x=>x.Day==day.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)&&x.IsReceived==1);KugouFeedback($"服务端日期 {day:yyyy-MM-dd} · 领取记录：{(received?"已领取":"未记录领取，不代表活动资格或权益生效")}；本次仅查询");})),
    NB("退出账号",()=>{KugouLifecycle(async()=>{RevokeKugouConsent();await a.Kugou.LogoutAsync();RevokeKugouHttpSession();ClearKugouQr();kugouAccountState.Text="已退出当前账号并撤销音源许可；Windows 保存登录仍保留";kugouVipState.Text="权益：未查询";});})),
   kugouVipState,
   T("首次连接集中告知并记住选择；隐私与权限可在设置中管理。",12,true),
   H(keyword,NB("搜索",()=>KugouAction(async()=>{DemandSdk(a);var found=await a.Kugou.SearchAsync(keyword.Text??"");kugouResults.Children.Clear();foreach(var song in found.Tracks){var track=song;kugouResults.Children.Add(V(T(track.Title+" · "+track.Artist,14),H(
    NB("授权并加入队列",()=>KugouAction(async()=>{DemandSdk(a);int id=await a.AdmitKugouAsync(track,"standard");await a.QueueAsync("append",id);KugouFeedback("已加入播放队列");})),
    NB("授权并播放",()=>KugouAction(async()=>{DemandSdk(a);int id=await a.AdmitKugouAsync(track,"standard");await a.PlayAsync(id);KugouFeedback("正在播放");})))));}KugouFeedback($"找到 {found.Tracks.Count} 首歌曲");}))),
   H(NB("酷狗 HTTP 本次会话许可（风险确认）",()=>KugouAction(async()=>{
      var allowed=await ApproveKugouHttpSessionAsync();
      KugouFeedback(allowed?"已明确允许本次运行中的已核对酷狗 CDN HTTP 音频；关闭软件或撤销即失效":"保持仅 HTTPS；HTTP 音频未授权");
    })),
    NB("撤销 HTTP 会话许可",()=>{RevokeKugouHttpSession();KugouFeedback("已撤销本次 HTTP 会话许可；后续 HTTP 音频将拒绝，直至你主动启用");})),
   H(NB("只读歌单刷新",()=>KugouAction(async()=>{DemandPlaylistConsent();await LoadKugouPlaylistsAsync(a);})),
    NB("查看本地歌单快照（离线）",()=>ShowOfflineKugouSnapshot(a))),
   H(NB("停用 DLC",()=>{KugouLifecycle(async()=>{RevokeKugouConsent();await a.DisableKugouAsync();RevokeKugouHttpSession();ClearKugouQr();kugouAccountState.Text="模块已停用；本地播放独立可用";kugouVipState.Text="权益：未查询";});}),
    NB("撤销本次授权",()=>{KugouLifecycle(async()=>{RevokeKugouConsent();await a.DisableKugouAsync();KugouFeedback("本次授权已撤销，歌单资料保留");});}),
    NB("移除 DLC",()=>{KugouLifecycle(()=>{RevokeKugouConsent();a.RemoveKugou();RevokeKugouHttpSession();ClearKugouQr();kugouAccountState.Text="本次进程已移除模块";return Task.CompletedTask;});})),
   kugouStatus,kugouResults,
   NH("KUGOU PLAYLISTS","酷狗官方歌单（只读）","按平台独立显示；不会与本地、QQ 音乐、网易云歌单混合。"),
   kugouOfficialPlaylists,
   new Expander{Header="开发者诊断（合成演练）",IsExpanded=false,Content=fixture});
  if(!KugouAvaloniaPlayer.Services.Startup.OfflineGuiApplication.SelfTest){
   foreach(var button in kugouPageCache.GetLogicalDescendants().OfType<Button>())if(button.Content is string label&&(label is "高级连接配置" or "离线二维码演练" or "查询领取状态"||label.StartsWith("导入我之前")))button.IsVisible=false;
  }
  return kugouPageCache;
 }
 void DemandSdk(CorePlaybackAdapter a){DemandPlaylistConsent();if(!a.Kugou.SdkConnected)throw new UnauthorizedAccessException("获准 SDK 连接未建立；未发送平台请求");}
 void KugouLifecycle(Func<Task> work){if(core is CorePlaybackAdapter current)current.Kugou.CancelPlaylistDetails();CancelKugouLogin();RunCore(async()=>{try{await work();}catch(Exception e){KugouFeedback("操作未完成："+e.GetType().Name);}});}
 internal void StartKugouQr(){
  if(kugouLoginRunning||kugouBusy){KugouFeedback("登录正在进行，可取消后重试");return;}
  if(core is not CorePlaybackAdapter a)return;
  try{DemandSdk(a);}catch(Exception e){KugouFeedback(e.Message);return;}
  kugouQrCancel=new CancellationTokenSource(KugouQrDeadline);var ct=kugouQrCancel.Token;kugouLoginRunning=true;
  KugouAction(async()=>{
   bool success=false;
   try{
    var qr=await a.Kugou.BeginQrAsync(ct);ClearKugouQr();
    if(qr.ImageBytes==null)throw new InvalidDataException("服务端未提供已验证的PNG二维码；未伪造授权码");
    using var pixels=new MemoryStream(qr.ImageBytes);kugouQrBitmap=new Bitmap(pixels);kugouQrImage!.Source=kugouQrBitmap;kugouQrImage.IsVisible=true;kugouAccountState!.Text="等待扫码；请仅确认你主动发起的授权";
    for(int poll=0;poll<30;poll++){ct.ThrowIfCancellationRequested();var state=await a.Kugou.PollQrAsync(ct);ct.ThrowIfCancellationRequested();
     if(state==QrLoginStatus.Success){success=true;if(KugouRiskSettings.Matches(KugouRiskSettings.Read())&&KugouRiskSettings.Read()!.SaveLogin&&a.Kugou.IsDirectSdk)a.Kugou.SaveDirectLogin();kugouAccountState.Text="登录成功；权益未查询，不自动领取或播放"+M206AccountLabel();break;}
     if(state==QrLoginStatus.Expired)throw new TimeoutException("二维码已过期");
     kugouAccountState.Text=state==QrLoginStatus.WaitingForConfirm?"已扫码，等待用户确认":"等待扫码";await Task.Delay(2100,ct);
    }
    if(!success)throw new TimeoutException("二维码查询次数已达上限");
   }catch(OperationCanceledException){kugouAccountState!.Text="登录已取消或超时，未自动重试";}
   catch(Exception e){kugouAccountState!.Text="登录未完成："+e.GetType().Name;}
   finally{ClearKugouQr();try{if(!success)await a.Kugou.CancelQrAsync();}finally{kugouLoginRunning=false;kugouQrCancel?.Dispose();kugouQrCancel=null;}}
  });
 }
}
