using KugouAvaloniaPlayer.Services;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 internal void R2ExitFailureTest(OfflineWindowRoot root,bool recovery){
  if(recovery){if(root.Player.IsPlayingAudio||!root.Coordinator.Player.IsStopped||AppSqliteStore.LoadValue("r2-close","ids")!=string.Join(",",root.Player.PlaybackQueue.Select(x=>x.QueueEntryId)))throw new IOException("Failed cache recovery");Program.Log("R2_CLOSE_RECOVERY",new{ok=true,count=root.Player.PlaybackQueue.Count,noAutoplay=true});Close();return;}
  root.Player.SetControlledQueue(root.ViewModel.Library.SelectedPlaylistSongs.Take(2).ToArray());root.Player.FlushControlledQueue();
  AppSqliteStore.SaveValue("r2-close","expected",AppSqliteStore.LoadValue("playback_queue","app")!);AppSqliteStore.SaveValue("r2-close","ids",string.Join(",",root.Player.PlaybackQueue.Select(x=>x.QueueEntryId)));
  using var c=AppDatabase.CreateConnection();using var q=c.CreateCommand();q.CommandText="CREATE TRIGGER r2_close_fail BEFORE INSERT ON kv_store WHEN NEW.scope='playback_queue' BEGIN SELECT RAISE(ABORT,'R2_OWNED_CLOSE_FAILURE'); END";q.ExecuteNonQuery();Program.Log("R2_CLOSE_FAILURE_INJECTED",new{ownProfile=true});Close();
 }
}
