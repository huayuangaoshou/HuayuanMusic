using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.Services;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 internal async Task M206BRiskTest(CorePlaybackAdapter adapter,Func<int> requests){
  int checks=0,prompts=0;bool restart=Environment.GetEnvironmentVariable("P17_RESTART")=="1";
  void Check(bool ok,string label){Program.Log(ok?"M206B_PASS":"M206B_FAIL",new{label});if(!ok)throw new Exception(label);checks++;}
  Window? Popup()=>((IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current!.ApplicationLifetime!).Windows.FirstOrDefault(w=>w!=this&&w.Title=="酷狗概念版 · 使用须知");
  async Task<Window> WaitPopup(){for(int i=0;i<100&&Popup()==null;i++)await Task.Delay(20);var w=Popup()??throw new Exception("Missing risk dialog");await Task.Delay(150);prompts++;return w;}
  void Click(Window w,string text)=>w.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()==text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  async Task Shot(Window w,string name){var path=Path.Combine(Program.Session,name+".ready");Program.Log("CAPTURE",new{name,hwnd=w.TryGetPlatformHandle()!.Handle.ToInt64(),w.RenderScaling});File.WriteAllText(path,"ready");for(int i=0;i<250&&!File.Exists(path+".done");i++)await Task.Delay(20);Check(File.Exists(path+".done"),"Screenshot acknowledged "+name);}
  int before=requests();
  Check(!kugouConsent.Approved&&!IsKugouHttpSessionApproved,"New process has no account permission or HTTP grant");
  if(!restart){
   Check(KugouRiskSettings.Read()==null,"First install has no risk record");var denied=EnsureKugouRiskAsync();var w=await WaitPopup();
   var http=w.GetVisualDescendants().OfType<CheckBox>().Single(x=>x.Name=="KugouRiskHttp");Check(http.IsChecked!=true,"HTTP is never preselected");
   Check(w.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text==KugouRiskText),"All seven risk topics on same page");
   var yes=w.GetVisualDescendants().OfType<Button>().Single(x=>x.Content?.ToString()=="我已了解，继续使用");
   Check(yes.Bounds.Height>0&&yes.TranslatePoint(new Avalonia.Point(0,yes.Bounds.Height),w)!.Value.Y<=w.ClientSize.Height,"Confirmation visible without scrolling");
   await Shot(w,"risk-first");Click(w,"取消");Check(!await denied&&KugouRiskSettings.Read()==null&&!kugouConsent.Approved&&requests()==before,"Cancel persists nothing, grants nothing and sends no request");
   var accepted=EnsureKugouRiskAsync();w=await WaitPopup();w.GetVisualDescendants().OfType<CheckBox>().Single(x=>x.Name=="KugouRiskHttp").IsChecked=true;Click(w,"我已了解，继续使用");Check(await accepted,"Concentrated confirmation succeeds");
   Check(KugouRiskSettings.Matches(KugouRiskSettings.Read())&&IsKugouHttpSessionApproved,"Version scope timestamp persisted; HTTP stays memory only");
   Check(!KugouRiskSettings.Read()!.SaveLogin,"Save-login is opt-in");
   Check(await ConfirmKugouHttpAsync("audio.kugoucdn.com")&&!await ConfirmKugouHttpAsync("kugoucdn.com.evil.test"),"Explicit HTTP remains limited to verified CDN");
  }else{
   Check(KugouRiskSettings.Matches(KugouRiskSettings.Read()),"Fresh process restores same-version same-scope record");
   Check(!await ConfirmKugouHttpAsync("audio.kugoucdn.com"),"Restart strictly refuses HTTP without a new session choice");
   Check(!kugouConsent.Approved,"Persisted knowledge alone does not authorize an account");
  }
  int shown=prompts;for(int i=0;i<8;i++)Check(await EnsureKugouRiskAsync()&&Popup()==null,"Normal repeated use has no risk modal "+i);
  Check(prompts==shown&&requests()==before,"Risk decisions do not touch platform transport");
  await kugouConsent.RequestAsync(EnsureKugouRiskAsync);Check(kugouConsent.Reconcile(true,"account-a","key-a"),"Proactive connect can bind session from known risk scope");
  Check(!kugouConsent.Reconcile(true,"account-b","key-b"),"Switching identity invalidates old account permission");ReconcileKugouConsent();
  Check(await EnsureKugouRiskAsync()&&Popup()==null,"Account change does not replay generic risk disclosure");
  var record=KugouRiskSettings.Read();Check(!KugouRiskSettings.Matches(record,"next-version"),"Risk version expansion requires disclosure");
  Check(!KugouRiskSettings.Matches(record,KugouRiskSettings.Version,KugouRiskSettings.Scope+",writeback"),"Scope expansion requires disclosure");
  if(restart){
   foreach(var stale in new[]{record! with{Version="old-risk-version"},record! with{Scope="network"}}){
    AppSqliteStore.SaveValue("dlc_privacy","kugou-concept",System.Text.Json.JsonSerializer.Serialize(stale));
    var expanded=EnsureKugouRiskAsync();var updated=await WaitPopup();Click(updated,"我已了解，继续使用");Check(await expanded,"Stale risk version or smaller scope actually reopens one dialog");
   }
  }
  KugouRiskSettings.SavePreference(true);Check(KugouRiskSettings.Read()!.SaveLogin,"Login preference persists independently of credentials");KugouRiskSettings.SavePreference(false);
  // No live factory can become executable via synthetic UI confirmation.
  bool refused=false;try{DemandM205Connection();}catch(UnauthorizedAccessException){refused=true;}Check(refused&&requests()==before,"Offline host still refuses real account factory");
  if(restart){
   await RevokeKugouRiskAsync();Check(KugouRiskSettings.Read()==null&&!kugouConsent.Approved&&!IsKugouHttpSessionApproved,"Revoke deletes knowledge and disables session and HTTP");
   var again=EnsureKugouRiskAsync();var w=await WaitPopup();await Shot(w,"risk-after-revoke");Click(w,"我已了解，继续使用");Check(await again&&KugouRiskSettings.Matches(KugouRiskSettings.Read()),"Explicit acceptance after revocation restores knowledge only");
   Check(!IsKugouHttpSessionApproved&&!await ConfirmKugouHttpAsync("audio.kugou.com"),"HTTPS-only choice never prompts or silently permits HTTP");
  }
  nightSettingsGroup="隐私与权限";Go("设置");await Task.Delay(100);await Shot(this,restart?"privacy-restart":"privacy-first");
  Check(KugouRiskSettings.Read()!.Scope==KugouRiskSettings.Scope,"Only disclosed scope persisted, no VIP or writes");
  Program.Log("M206B_COMPLETE",new{checks,prompts,restart,realRequests=0});
 }
}
