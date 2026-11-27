using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HuayuanPreview.R2;
using KugouAvaloniaPlayer.Services.Startup;
using System.Security.Cryptography;
namespace HuayuanPreview;
internal sealed record ImportEntry(string Path,long Length,string Hash,string Status);
public sealed partial class PreviewWindow {
 CancellationTokenSource? importCancel;Task importTask=Task.CompletedTask;TextBlock? importFeedback;ModalOverlay.Session<bool>? importDialog;
 internal async Task CancelImportAndWait(){importCancel?.Cancel();await importTask;importDialog?.Dismiss();}
 internal static async Task<ImportEntry[]> PreviewImport(string[] selected,bool folder,CancellationToken cancel){
  if(!OfflineStartupProfile.Current.Import||!OfflineStartupProfile.Current.LiveGuiPermit)throw new IOException("No explicit import capability");
  var paths=new List<string>();int directories=0,visited=0;
  void Validate(string path){if(!Path.IsPathFullyQualified(path)||path.StartsWith(@"\\")||path.AsSpan(2).Contains(':'))throw new IOException("Only selected local paths");var d=new DriveInfo(Path.GetPathRoot(path)!);if(d.DriveType is not (DriveType.Fixed or DriveType.Removable))throw new IOException("Network drives refused");OfflineStartupProfile.RejectLinks(path);}
  void Walk(string dir,int depth){cancel.ThrowIfCancellationRequested();if(++directories>2000)throw new IOException("目录数量超过2000，请缩小选择范围");Validate(dir);if(depth>8||paths.Count>500)throw new IOException("目录预览上限500首/8层，请选择更小目录");using var locked=OfflineMediaImport.LockDirectories(dir);foreach(var p in Directory.EnumerateFileSystemEntries(dir)){cancel.ThrowIfCancellationRequested();if(++visited>10000)throw new IOException("文件项超过10000，请缩小选择范围");OfflineStartupProfile.RejectLinks(p);if(Directory.Exists(p))Walk(p,depth+1);else if(OfflineMediaImport.Supported(p)){paths.Add(p);if(paths.Count>500)throw new IOException("每次预览最多500首");}}}
  foreach(var p in selected){Validate(p);if(folder){if(Path.GetFullPath(p).TrimEnd('\\')==Path.GetPathRoot(p)!.TrimEnd('\\'))throw new IOException("禁止整盘导入");Walk(p,0);}else paths.Add(p);}
  if(paths.Count>500)throw new IOException("每次最多选择500首");
  var result=new List<ImportEntry>();long bytes=0;
  foreach(var p in paths.Distinct(StringComparer.OrdinalIgnoreCase)){
   cancel.ThrowIfCancellationRequested();try{Validate(p);if(!OfflineMediaImport.Supported(p))throw new IOException("不支持的格式");using var parents=OfflineMediaImport.LockDirectories(Path.GetDirectoryName(p)!);await using var f=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);OfflineMediaImport.CheckHandle(f.SafeFileHandle,p);if(f.Length<=0||f.Length>OfflineMediaImport.MaxBytes)throw new IOException("空文件或超过256MiB");bytes+=f.Length;if(bytes>2L*1024*1024*1024)throw new IOException("单次预览总量超过2GiB");var hash=Convert.ToHexString(await SHA256.HashDataAsync(f,cancel));result.Add(new(p,f.Length,hash,"待确认"));}
   catch(OperationCanceledException){throw;}catch(Exception e){result.Add(new(p,0,"","跳过："+e.Message));}
  }
  return result.ToArray();
 }
 internal async Task<string[]> ConfirmImport(ImportEntry[] entries,CancellationToken cancel){
  var results=new List<string>();foreach(var batch in entries.Chunk(20)){if(cancel.IsCancellationRequested){results.Add("已取消；此前成功项保留");break;}var leases=new List<IDisposable>();var approved=new List<string>();
   try{foreach(var entry in batch){if(entry.Hash.Length==0){results.Add(Path.GetFileName(entry.Path)+"："+entry.Status);continue;}try{var parents=OfflineMediaImport.LockDirectories(Path.GetDirectoryName(entry.Path)!);leases.Add(parents);OfflineStartupProfile.RejectLinks(entry.Path);var f=new FileStream(entry.Path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);leases.Add(f);OfflineMediaImport.CheckHandle(f.SafeFileHandle,entry.Path);if(f.Length!=entry.Length||Convert.ToHexString(await SHA256.HashDataAsync(f,cancel))!=entry.Hash)throw new IOException("预览后源文件变化，请重新选择");approved.Add(entry.Path);}catch(OperationCanceledException){throw;}catch(Exception e){results.Add(Path.GetFileName(entry.Path)+"：失败（"+e.Message+"）");}}
    if(approved.Count>0){await core.ImportFilesAsync(approved.ToArray(),cancel);results.AddRange(core.State.ImportStatus.Split('\n'));}
   }catch(OperationCanceledException){results.Add("已取消；未完成副本由原导入器清理");break;}catch(Exception e){results.Add("批次失败，保留已成功项："+e.Message);break;}finally{foreach(var lease in leases)lease.Dispose();}
   if(importFeedback!=null)importFeedback.Text=string.Join('\n',results.TakeLast(8));
  }
  return results.ToArray();
 }
 async void SelectImport(bool folder){
  if(!OfflineStartupProfile.Current.Import){Notify("当前旧回归profile无导入许可");return;}if(!importTask.IsCompleted||importDialog!=null){Notify("请先完成或取消现有导入");return;}
  try{
   var start=await StorageProvider.TryGetFolderFromPathAsync(OfflineStartupProfile.Current.Media)??throw new IOException("无法确定安全选择器起始目录");string[] selected;
   if(folder){var pick=await StorageProvider.OpenFolderPickerAsync(new(){Title="选择音乐文件夹：只预览，确认后复制",AllowMultiple=false,SuggestedStartLocation=start});selected=pick.Select(x=>x.TryGetLocalPath()??throw new IOException("拒绝非本地来源")).ToArray();foreach(var item in pick)item.Dispose();}
   else{var pick=await StorageProvider.OpenFilePickerAsync(new(){Title="选择WAV / MP3 / FLAC：只读导入副本",AllowMultiple=true,SuggestedStartLocation=start,FileTypeFilter=[new("本地音频"){Patterns=["*.wav","*.mp3","*.flac"]}]});selected=pick.Select(x=>x.TryGetLocalPath()??throw new IOException("拒绝非本地来源")).ToArray();foreach(var item in pick)item.Dispose();}
   if(selected.Length==0){Notify("已取消选择，未导入");return;}
   await ShowImportPreviewAsync(
    token => Task.Run(() => PreviewImport(selected, folder, token), token), ConfirmImport);
  }catch(Exception e){Notify("导入未开始："+e.Message);}
 }

 internal async Task ShowImportPreviewAsync(Func<CancellationToken, Task<ImportEntry[]>> load,
  Func<ImportEntry[], CancellationToken, Task<string[]>> import)
 {
  if (importDialog != null || !importTask.IsCompleted) return;
  var cancellation = new CancellationTokenSource();
  var token = cancellation.Token;
  ImportEntry[] entries = [];
  var rows = new TextBlock { Text = "正在生成受限预览…", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
  var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
  var confirm = new Button { Content = "确认只读复制并导入", Classes = { "primary" }, IsEnabled = false, IsDefault = true };
  var cancel = new Button { Content = "取消", IsCancel = true };
  AutomationProperties.SetName(confirm, "确认只读复制并导入");
  AutomationProperties.SetName(cancel, "取消");
  var description = new TextBlock
  {
   Text = "先预览，再确认。只操作本次明确选择的文件。最多500首/2GiB，单文件256MiB。",
   TextWrapping = Avalonia.Media.TextWrapping.Wrap
  };
  var list = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
  var footer = new StackPanel { Spacing = DesignTokens.Space2, Children = { status, H(confirm, cancel) } };
  var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = DesignTokens.Space3,
   Children = { description, list, footer } };
  Grid.SetRow(list, 1);
  Grid.SetRow(footer, 2);
  var dialog = ModalOverlay.TryShow<bool>(this, content, new("导入预览 · 不修改源文件", 650, 520, cancel), RequestClose);
  if (dialog == null)
  {
   cancellation.Dispose();
   return;
  }
  importCancel = cancellation;
  importDialog = dialog;
  var cleanup = Task.CompletedTask;
  dialog.Closed += () => cleanup = ReleaseImportAsync();
  cancel.Click += (_, _) => dialog.Dismiss();
  confirm.Click += async (_, _) =>
  {
   if (!dialog.IsOpen || !confirm.IsEnabled || !importTask.IsCompleted) return;
   cancel.Focus();
   confirm.IsEnabled = false;
   importFeedback = rows;
   importTask = RunImportAsync();
   try { await importTask; }
   finally
   {
    importFeedback = null;
    cancel.Content = "关闭";
    AutomationProperties.SetName(cancel, "关闭");
   }
  };

  importTask = LoadPreviewAsync();
  await dialog.Completion;
  await cleanup;

  async Task ReleaseImportAsync()
  {
   cancellation.Cancel();
   try { await importTask; }
   finally
   {
    cancellation.Dispose();
    importDialog = null;
    importCancel = null;
   }
  }

  bool RequestClose()
  {
   if (importTask.IsCompleted) return true;
   cancellation.Cancel();
   status.Text = "正在取消导入，请等待安全释放";
   status.IsVisible = true;
   Notify(status.Text);
   return false;
  }

  async Task LoadPreviewAsync()
  {
   try
   {
    entries = await load(token);
    rows.Text = string.Join('\n', entries.Select(entry =>
     Path.GetFileName(entry.Path) + " · " + entry.Status + " · " + (entry.Length / 1048576d).ToString("F2") + "MiB"));
    confirm.IsEnabled = !token.IsCancellationRequested && entries.Any(entry => entry.Hash.Length > 0);
    Notify($"预览 {entries.Length} 项，尚未导入");
   }
   catch (OperationCanceledException) when (token.IsCancellationRequested) { rows.Text = "已取消预览"; }
   catch (Exception error) { rows.Text = "预览未完成：" + error.Message; }
   finally { status.IsVisible = false; }
  }

  async Task RunImportAsync()
  {
   try
   {
    rows.Text = string.Join('\n', await import(entries, token));
    Notify("导入完成/取消：可查看逐项结果，成功项已保留");
   }
   catch (OperationCanceledException) when (token.IsCancellationRequested) { rows.Text = "已取消；此前成功项保留"; }
   catch (Exception error) { rows.Text = "导入未完成：" + error.Message; }
   finally { status.IsVisible = false; }
  }
 }
}
