using Avalonia.Controls;
using HuayuanMusic.Provider.Kugou;

namespace HuayuanPreview;

public sealed partial class PreviewWindow {
 TextBlock? kugouStatus;StackPanel? kugouResults;bool kugouBusy;
 Control KugouFixturePage(){
  if(core is not CorePlaybackAdapter adapter)return T("仅受控宿主可用",16);
  kugouStatus=T("DLC 默认禁用。此候选全部为 Mock 服务端，非真实账号/会员/音乐。",13,true);
  kugouResults=V();var input=new TextBox{Text="测试",PlaceholderText="中文搜索 · Mock",Width=200};
  return V(
   NH("KUGOU · SDK / AUTH INTEGRATION","酷狗 DLC 集成候选","现有 KuGou.Net + 已有 Auth 网关 + 唯一播放核心"),
   T("SDK/Auth 分支已编译接入。尚无获准应用身份/设备与真实测试批准，不建立真实传输。",13,true),
   T("连接顺序：授权 QR → 内存会话 → SDK 搜索/歌单/歌词 → 单曲 Auth → Registry → BASS。",12,true),
   NB("账号授权入口 · 查看接入条件",()=>KugouFeedback("REAL_PENDING：未配置适用平台身份/签名/设备许可及单独用户批准；没有发出二维码或账号请求。")),
   T("下方保留的是上轮离线模拟入口，用于本地回归，不是真实酷狗登录或试听。",12,true),
   NH("KUGOU CONCEPT · DLC RESEARCH","酷狗概念版","独立模块 / 内存会话 / 零真实网络 / 真实 BASS 播放自产 WAV"),
   T("真实登录、官方活动规则、应用身份与音源授权尚未验证，REAL_PENDING。",12,true),
   H(NB("启用可信内置模块",()=>{adapter.EnableKugou();KugouFeedback("模块已启用，未登录，不自动播放");}),
    NB("模拟授权 A",()=>KugouFixtureAction(async()=>{await adapter.Kugou.LoginFixtureAsync("demo-a");KugouFeedback("模拟会话 A，非真实登录");})),
    NB("模拟授权 B",()=>KugouFixtureAction(async()=>{await adapter.Kugou.LoginFixtureAsync("demo-b");KugouFeedback("模拟会话 B，与 A 隔离");}))),
   H(NB("查询模拟权益",()=>KugouFixtureAction(async()=>{var e=await adapter.Kugou.RefreshEntitlementAsync();KugouFeedback("ENTITLEMENT_ACTIVE="+e.Active+" · "+e.Product+" · "+e.Until);})),
    NB("主动领取（仅 Mock）",()=>KugouFixtureAction(async()=>{var e=await adapter.Kugou.ClaimTodayAsync(true);KugouFeedback("CLAIM="+e.State+" / ENTITLEMENT_ACTIVE="+e.Entitlement.Active+"（模拟不等于真实成功）");}))),
   H(input,NB("搜索",()=>KugouFixtureAction(async()=>{var found=await adapter.Kugou.SearchAsync(input.Text??"");kugouResults.Children.Clear();foreach(var t in found.Tracks){var track=t;kugouResults.Children.Add(V(T(t.Title+" / "+t.Artist,14),H(NB("权限并加入队列",()=>KugouFixtureAction(async()=>{int id=await adapter.AdmitKugouAsync(track,"standard");await adapter.QueueAsync("append",id);KugouFeedback("已追加 Mock 曲目；未自动播放。混合队列暂不持久化。");})),NB("授权并试听自产音",()=>KugouFixtureAction(async()=>{int id=await adapter.AdmitKugouAsync(track,"standard");await adapter.PlayAsync(id);KugouFeedback("Mock 响应 → 本体真实 BASS。真人听觉仍待验；非平台歌曲。");})))));}KugouFeedback($"找到 {found.Tracks.Count} 条 Mock 元数据，尚不代表播放授权");}))),
   H(NB("只读歌单刷新（Mock）",()=>KugouFixtureAction(async()=>{var lists=await adapter.Kugou.ReadAsync(adapter.Kugou.Account??"");kugouResults.Children.Clear();foreach(var list in lists)kugouResults.Children.Add(T(list.Name+" / "+list.Tracks.Count+" 首（仅元数据）",14));KugouFeedback("按账号隔离的内存快照；磁盘缓存待接入");})),
    NB("登出",()=>KugouFixtureAction(async()=>{await adapter.Kugou.LogoutAsync();KugouFeedback("会话与所有播放许可已撤销");})),
    NB("停用 / 撤销许可",()=>KugouFixtureAction(async()=>{await adapter.DisableKugouAsync();KugouFeedback("已停用；本地播放器保持独立可用");}))),
   NB("移除内置模块（本次进程）",()=>{if(adapter.Kugou.SdkConnected){KugouFeedback("SDK连接中，模拟入口不操作真实连接");return;}adapter.RemoveKugou();KugouFeedback("模块已移除，许可撤销；重启研究候选可重新注册。未删除本地数据。");}),
   kugouStatus,kugouResults,
   T("临时研究限制：自动领取关闭；无真实 QR 登录；无真实封面/HTTP；平台收藏与历史、混合队列持久化、永久歌单写入未开放。",12,true));
 }
 void KugouFixtureAction(Func<Task> action){if(core is CorePlaybackAdapter a&&a.Kugou.SdkConnected){KugouFeedback("请先退出 SDK 连接；模拟入口不会调用真实账号服务");return;}KugouAction(action);}
 void KugouFeedback(string text){if(kugouStatus!=null)kugouStatus.Text=text;}
 void KugouAction(Func<Task> action){if(kugouBusy){KugouFeedback("操作进行中，请稍候");return;}kugouBusy=true;RunCore(async()=>{try{await action();}catch(Exception ex){Program.Log("KUGOU_ACTION_ERROR",new{type=ex.GetType().Name});KugouFeedback(KugouAvaloniaPlayer.Services.Startup.OfflineGuiApplication.SelfTest?"操作未完成："+ex.GetType().Name:"操作未完成，请检查网络、账号或歌曲权限后重试。歌单已保留。");}finally{kugouBusy=false;}});}
}
