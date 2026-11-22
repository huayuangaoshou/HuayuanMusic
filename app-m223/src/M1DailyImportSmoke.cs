using System.Security.Cryptography;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
using ManagedBass;
namespace HuayuanPreview;

// Synthetic-only acceptance of the same guarded UI importer used by the daily launcher.
// Never opens personal folders, never starts audio or uses a platform account.
public sealed partial class PreviewWindow {
 internal async Task M1DailyImportSmoke(OfflineWindowRoot root,CorePlaybackAdapter adapter){
  var checks=0;
  void Check(bool ok,string label){Program.Log("M1_DAILY_IMPORT_CHECK",new{ok,label});if(!ok)throw new InvalidOperationException(label);checks++;}
  var restart=Environment.GetEnvironmentVariable("M1_DAILY_RESTART")=="1";
  var fixture=Path.Combine(Program.Root,"fixtures","formats","plain.wav");
  var originalHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture)));
  try {
   Check(OfflineGuiApplication.SelfTest&&OfflineStartupProfile.Current.Import&&
         Path.GetFileName(OfflineStartupProfile.Current.Root).StartsWith("p17-body-r3-manual-test-",StringComparison.Ordinal),
         "Explicit synthetic-only M1 test profile and import permit");
   Check(!root.Coordinator.Player.IsPlaying&&Bass.CurrentDevice==-1,"No audio device opened or auto-played");
   if(!restart){
    Check(adapter.State.Tracks.Length==0&&adapter.State.Lists.Length==0,"Fresh M1 daily library begins empty");
    var chosen=await PreviewImport([fixture],false,CancellationToken.None);
    Check(chosen.Length==1&&chosen[0].Hash.Length==64&&adapter.State.Tracks.Length==0,
          "Preview selected synthetic WAV only, no premature database changes");
    var result=await ConfirmImport(chosen,CancellationToken.None);
    Check(result.Any(x=>x.Contains("成功",StringComparison.Ordinal))&&adapter.State.Error.Length==0,
          "Confirmed source copy and production importer succeed");
    Check(adapter.State.Tracks.Length==1&&adapter.State.Lists.Any(x=>x.Name=="本地导入"&&x.SongIds.Length==1),
          "Empty library creates exactly one imported playlist after explicit confirmation");
    var track=adapter.State.Tracks.Single();
    await adapter.QueueAsync("append",track.Id);
    Check(adapter.State.Error.Length==0&&adapter.State.QueueEntries.Length==1&&
          adapter.State.QueueEntries[0].EntryId!=Guid.Empty,"Production queue accepts imported track with occurrence ID");
    var entry=adapter.State.QueueEntries[0].EntryId;
    AppSqliteStore.SaveValue("m1-daily","expected-occurrence",entry.ToString("D"));
    await adapter.PlaylistAsync("save","M1 合成导入验收");
    Check(adapter.State.Error.Length==0&&adapter.State.Lists.Any(x=>x.Name=="M1 合成导入验收"&&
          x.SongIds.SequenceEqual([track.Id])&&x.EntryIds?.Length==1),
          "Save full production queue to SQLite playlist entry");
   }else{
    Check(adapter.State.Tracks.Length==1,"Imported synthetic WAV persists across process restart");
    Check(adapter.State.Lists.Length==2&&adapter.State.Lists.Any(x=>x.Name=="本地导入")&&
          adapter.State.Lists.Any(x=>x.Name=="M1 合成导入验收"&&x.EntryIds?.Length==1),
          "Both imported and saved playlists persist across restart");
    var expected=AppSqliteStore.LoadValue("m1-daily","expected-occurrence");
    Check(adapter.State.QueueEntries.Length==1&&
          adapter.State.QueueEntries[0].EntryId.ToString("D")==expected,
          "Production queue occurrence ID survives restart without autoplay");
   }
   Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture)))==originalHash,
         "Original synthetic source bytes were not modified");
   Check(OfflineStartupNetwork.Requests==1&&root.Coordinator.Player.IsStopped&&Bass.CurrentDevice==-1,
         "No real network and no audio device opened");
   Program.Log("M1_DAILY_IMPORT_COMPLETE",new{checks,restart,tracks=adapter.State.Tracks.Length,
       lists=adapter.State.Lists.Length,queue=adapter.State.QueueEntries.Length,noAutoplay=!adapter.State.Playing});
  }finally{Close();}
 }
}
