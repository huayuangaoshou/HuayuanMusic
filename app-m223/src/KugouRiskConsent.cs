using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HuayuanPreview.R2;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 private static bool HasConfirmedKugouScope(KugouRiskRecord? record) =>
  record != null && KugouRiskSettings.Matches(record, version: record.Version);

 internal const string KugouRiskText="第三方服务：本 DLC 并非酷狗官方客户端；第三方接口可能变化、失效，账号也可能受到平台限制。\n\n账号与隐私：连接会读取本应用保存的登录或由你扫码；向平台发送必要的账号、搜索、歌单和歌曲元数据。保存登录使用 Windows 凭据保护，共用电脑仍有风险；不读取其他软件账号。\n\n播放范围：只读同步官方歌单、搜索及你主动选择的歌曲播放。版权、地区、会员与歌曲资格由平台验证；登录不代表拥有所有歌曲权益。\n\n网络安全：默认仅 HTTPS。HTTP 明文可能泄露临时音源地址并遭监听或篡改；仅在你下方明确选择后，本次会话可使用已验证 CDN。仍优先 HTTPS，不附账号凭据、不跟随重定向，不允许任意 HTTP。\n\n资料与撤销：停用或退出将撤销账号会话和临时音源，保留离线歌单与本地音乐；缓存可能失效，请保留备份。仅使用本候选受信任 DLC，不运行未知插件。\n\n功能边界：登录后，应用每日首次连接时自动尝试领取当日免费 VIP（听歌领 VIP 活动，每日一次、失败不重试，与上游项目行为一致）；不保证领取成功，不执行官方歌单写回。删除保存登录仍需单独确认。";
 internal async Task<bool> EnsureKugouRiskAsync(){
  if(HasConfirmedKugouScope(KugouRiskSettings.Read()))return true;
  return await ShowKugouRiskAsync();
 }
 internal async Task<bool> ShowKugouRiskAsync(){
  if(!IsVisible||!IsEnabled)return false;
  var prior=KugouRiskSettings.Read();
  var notice=new TextBlock{Text=KugouRiskText,TextWrapping=TextWrapping.Wrap,FontSize=13};
  var http=new CheckBox{Name="KugouRiskHttp",Content="本次会话允许已验证 CDN 的 HTTP 音频",IsChecked=false};
  var save=new CheckBox{Name="KugouRiskSaveLogin",Content="扫码成功后保存登录（Windows 凭据；可随时关闭偏好）",IsChecked=prior?.SaveLogin==true};
  var yes=new Button{Content="我已了解，继续使用"};var no=new Button{Content="取消"};
  var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12,HorizontalAlignment=HorizontalAlignment.Right};actions.Children.Add(no);actions.Children.Add(yes);
  var body=new StackPanel{Spacing=12};
  body.Children.Add(new TextBlock{Text=prior!=null&&!HasConfirmedKugouScope(prior)?"权限范围已更新，请确认当前范围：账号会话、网络、只读歌单、搜索、主动播放与 HTTPS。":"账号会话、网络、只读歌单、搜索、主动播放与 HTTPS",TextWrapping=TextWrapping.Wrap,FontSize=12});body.Children.Add(notice);body.Children.Add(http);body.Children.Add(save);
  body.Children.Add(new TextBlock{Text="同一权限范围的知情记录保存在应用资料中；可在设置 → 隐私与权限查看或撤销。HTTP 关闭程序或撤权后失效。",TextWrapping=TextWrapping.Wrap,FontSize=12});body.Children.Add(actions);
  if (!await ModalOverlay.ShowAsync(this, new ScrollViewer { Content = body },
          new("酷狗概念版 · 使用须知", 720), yes, no, () => true, defaultConfirm: false)) return false;
  KugouRiskSettings.Confirm(save.IsChecked==true); // Persistence failure must not grant permission.
  if(http.IsChecked==true)Volatile.Write(ref kugouHttpSessionAllowed,1);
  else RevokeKugouHttpSession();
  return true;
 }
 async Task RevokeKugouRiskAsync(){
  RevokeKugouConsent();CancelKugouLogin();
  try{KugouRiskSettings.Revoke();}finally{if(core is CorePlaybackAdapter a)await a.DisableKugouAsync();}
 }
 Control KugouPrivacyPage(){
  var record=KugouRiskSettings.Read();var valid=KugouRiskSettings.Matches(record);
  var save=new ToggleSwitch{Content="扫码成功后保存登录",IsChecked=valid&&record!.SaveLogin};
  save.IsEnabled=valid;save.Click+=(_,_)=>KugouRiskSettings.SavePreference(save.IsChecked==true);
  var http=new ToggleSwitch{Content="本次会话允许已验证 CDN 的 HTTP 音频",IsChecked=IsKugouHttpSessionApproved};
  http.Click+=(_,_)=>{if(http.IsChecked!=true)RevokeKugouHttpSession();else {http.IsChecked=false;RunCore(async()=>{if(await ShowKugouRiskAsync())Go("设置");});}};
  return V(T("隐私与权限 · 酷狗概念版",18,false,true),T(valid?$"已确认 {record!.ConfirmedAt.ToLocalTime():yyyy-MM-dd HH:mm} · 说明 {record.Version}":"尚未确认当前风险说明",12,true),
   T("范围：网络、账号会话、歌单只读、搜索、主动播放及 HTTPS。知情记录不等于有效登录或歌曲权益。",12,true),
   NB("查看使用须知与选项",()=>RunCore(async()=>{await ShowKugouRiskAsync();Go("设置");})),save,T("关闭保存偏好不删除已有 Windows 登录；可在平台页明确保存或确认删除。",12,true),http,
   NB("撤销风险确认并停用 DLC",()=>RunCore(async()=>{await RevokeKugouRiskAsync();Go("设置");})),T("撤销后下次主动连接重新告知；已同步歌单和本地资料保留。",12,true));
 }
}
