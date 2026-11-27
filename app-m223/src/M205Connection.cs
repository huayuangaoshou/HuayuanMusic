using Avalonia.Controls;
using Avalonia.Layout;
using HuayuanPreview.R2;
using HuayuanMusic.Provider.Kugou;
using KugouAvaloniaPlayer.Services.Startup;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 readonly KugouSessionConsent kugouConsent=new();
 internal void ConfigureM205Connection(CorePlaybackAdapter adapter){
  void ReportArtwork(string reason)=>KugouArtwork.Diagnostic(reason);
  HuayuanMusic.Provider.Contracts.ArtworkUrl.Diagnostic+=ReportArtwork;Closed+=(_,_)=>HuayuanMusic.Provider.Contracts.ArtworkUrl.Diagnostic-=ReportArtwork;
  adapter.DirectKugouConnection=()=>{DemandM205Connection();var sdk=new DirectSdkGateway(ConfirmKugouHttpAsync,ApproveM206AudioAsync);sdk.PlaylistArtworkDiagnostic+=reason=>KugouArtwork.Diagnostic(reason);return sdk;};
  adapter.ApprovedKugouConnection=()=>{DemandM205Connection();return new ApprovedKugouConnectionProvider(OfflineStartupProfile.Current.Root).Connect();};
  adapter.Kugou.Revoked+=()=>Avalonia.Threading.Dispatcher.UIThread.Post(ReconcileKugouConsent);
 }
 void ReconcileKugouConsent(){if(core is CorePlaybackAdapter a&&!kugouConsent.Reconcile(a.Kugou.SdkConnected,a.Kugou.Account,a.Kugou.DirectAccountCacheKey)){RevokeArtwork();RevokeKugouHttpSession();foreach(var button in kugouPlaylistPlayButtons)button.IsEnabled=false;}}
 void RevokeKugouConsent(){RevokeArtwork();kugouConsent.Revoke();RevokeKugouHttpSession();foreach(var button in kugouPlaylistPlayButtons)button.IsEnabled=false;}
 void DemandPlaylistConsent(){if(OfflineGuiApplication.SelfTest)return;ReconcileKugouConsent();if(!kugouConsent.Approved)throw new UnauthorizedAccessException("请先连接并授权酷狗");}
 async Task<bool> ApproveM206AudioAsync(){
  if(OfflineGuiApplication.SelfTest)return false;
  if(!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(ApproveM206AudioAsync);
  ReconcileKugouConsent();return kugouConsent.Approved;
 }
 internal void DemandM205Connection(){throw new UnauthorizedAccessException("P2A 仅支持合成离线会话");}
 Task<bool> ApproveM205ConnectionAsync()=>Task.FromResult(false);
 internal async Task<bool> ConfirmM205Async(string title,string message){
  if(!IsVisible||!IsEnabled)return false;
  var yes=new Button{Content="明确批准本次操作"};var no=new Button{Content="取消"};
  var panel=new StackPanel{Spacing=18};
  panel.Children.Add(new TextBlock{Text=message,TextWrapping=Avalonia.Media.TextWrapping.Wrap});
  var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};row.Children.Add(no);row.Children.Add(yes);panel.Children.Add(row);
  return await ModalOverlay.ShowAsync(this, new ScrollViewer { Content=panel }, new(title, 520),
      yes, no, () => true, defaultConfirm: false);
 }
}
