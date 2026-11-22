using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using KugouAvaloniaPlayer.Views;

namespace KugouAvaloniaPlayer.Services.Startup;

// Runs inside the real Program/App/window, only with selftest + import permission.
internal static class OfflineMediaImportExercise
{
    internal static async Task RunAsync(OfflinePlayerWindow window,OfflineWindowRoot root)
    {
        var p=OfflineStartupProfile.Current;var vm=root.ViewModel;var engine=root.Coordinator.Player;int checks=0;
        void Check(bool value,string name){OfflineGuiApplication.Audit(value?"FORMAT_PASS":"FORMAT_FAIL",name);if(!value)throw new InvalidOperationException(name);checks++;}
        Check(window.IsVisible&&window.TryGetPlatformHandle()?.Handle!=IntPtr.Zero,"actual formal App native window");
        Check(!engine.IsPlaying&&root.Queue.PlaybackQueue.Count==0,"no autoplay or old queue");
        var input=Path.Combine(p.Root,"selected-synthetic");
        var paths=new[]{"tagged.mp3","tagged.flac","plain.wav","untagged.mp3"}.Select(n=>Path.Combine(input,n)).ToArray();
        var hashes=paths.Select(OfflineMediaImport.Hash).ToArray();
        var existing=vm.Library.LocalLibraryPlaylists.FirstOrDefault(v=>v.Name=="P16 格式验收");
        if(existing==null){vm.PlaylistName="P16 格式验收";await window.ClickAsync("CreateButton");}
        else{vm.SelectedPlaylist=existing;await vm.ActAsync("Open");Check(vm.Library.SelectedPlaylistSongs.Count==4,"cross-process four imported songs restored before new import");}
        await vm.ImportSelectedAsync(paths,CancellationToken.None);
        Check(vm.Library.SelectedPlaylistSongs.Count==4,"MP3 FLAC WAV and untagged MP3 imported through production library");
        await vm.ImportSelectedAsync(paths,CancellationToken.None);
        Check(vm.Library.SelectedPlaylistSongs.Count==4&&vm.Status.Contains("重复"),"duplicate content reuses copy and playlist membership");
        await vm.ImportSelectedAsync(new[]{Path.Combine(input,"broken.mp3"),Path.Combine(input,"oversize-tag.mp3")},CancellationToken.None);
        Check(vm.Status.Contains("失败")&&vm.Library.SelectedPlaylistSongs.Count==4,"invalid and oversized metadata rejected without losing songs");
        foreach(var rejected in new[]{"missing.mp3","oversize.wav","tagged.mp3:stream"})
        {
            await vm.ImportSelectedAsync(new[]{Path.Combine(input,rejected)},CancellationToken.None);
            Check(vm.Status.Contains("失败")&&vm.Library.SelectedPlaylistSongs.Count==4,"reject unsafe selected source "+rejected);
        }
        var linked=Path.Combine(@"D:\AI\Projects\HuayuanMusic\.p1-work\p16\selected-sources",Path.GetFileName(p.Root),"linked","tagged.mp3");
        await vm.ImportSelectedAsync(new[]{linked},CancellationToken.None);
        Check(vm.Status.Contains("失败"),"real source junction rejected without reading linked target");
        using(var writer=new FileStream(paths[0],FileMode.Open,FileAccess.Write,FileShare.ReadWrite))
        {
            await vm.ImportSelectedAsync(new[]{paths[0]},CancellationToken.None);
            Check(vm.Status.Contains("失败"),"refuse source already open for writing/replacement");
        }
        await vm.ImportSelectedAsync(Enumerable.Repeat(paths[0],21).ToArray(),CancellationToken.None);
        Check(vm.Status.Contains("20"),"batch limit before reading sources");
        await vm.ImportSelectedAsync(Array.Empty<string>(),CancellationToken.None);Check(vm.Status.Contains("取消"),"empty file selection changes no content");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();await vm.ImportSelectedAsync(paths,cancel.Token);Check(vm.Status.Contains("取消"),"pre-cancelled batch reads no new source");}
        var ledgerBefore=OfflineMediaImport.Hash(Path.Combine(p.Root,"imports.v1.tsv"));
        using(var cancel=new CancellationTokenSource())
        {
            var progress=new CancelOnChunk(cancel);bool interrupted=false;
            try{await Task.Run(()=>OfflineMediaImport.CopySelectedAsync(paths[2],cancel.Token,progress));}catch(OperationCanceledException){interrupted=true;}
            Check(interrupted&&progress.Bytes>0&&OfflineMediaImport.Hash(Path.Combine(p.Root,"imports.v1.tsv"))==ledgerBefore,"cancel during real streaming copy leaves ledger unchanged");
        }
        foreach(var song in vm.Library.SelectedPlaylistSongs.ToArray())
        {
            var name=Path.GetFileName(song.LocalFilePath)??throw new InvalidDataException("Missing imported path");var ext=Path.GetExtension(name);
            Check(p.AllowsMedia(song.LocalFilePath)&&song.DurationSeconds>11&&song.DurationSeconds<13,"admitted owned "+name+" duration");
            if(name.StartsWith("tagged"))
            {
                Check(song.Name=="花园 Star 🌙"&&song.Singer.Contains("合成 Artist Ω")&&song.AlbumName=="Huayuan 测试专辑","ATL Unicode title artist album "+ext);
                Check(!string.IsNullOrEmpty(song.Cover)&&song.Cover.Contains("embedded",StringComparison.OrdinalIgnoreCase),"ATL embedded cover "+ext);
            }
            if(name=="untagged.mp3")Check(song.Name=="untagged"&&song.Singer=="未知艺术家","missing tags filename fallback");
            vm.SelectedSong=song;await window.ClickAsync("PlayButton");await Task.Delay(500);
            var clock=engine.GetPosition().TotalSeconds;await Task.Delay(300);
            Check(engine.IsPlaying&&engine.ActiveSource==song.LocalFilePath&&vm.Player.CurrentPlayingSong?.LocalFilePath==song.LocalFilePath&&engine.GetPosition().TotalSeconds>clock+.1&&engine.GetActiveAnalysisSnapshot().Rms>0,"real BASS source identity decode clock and nonzero PCM "+name);
            await window.ClickAsync("PauseButton");clock=engine.GetPosition().TotalSeconds;await Task.Delay(160);
            Check(engine.IsPaused&&Math.Abs(clock-engine.GetPosition().TotalSeconds)<.06,"native pause "+name);
            window.FindControl<Slider>("SeekSlider")!.Value=5;await Task.Delay(100);Check(Math.Abs(engine.GetPosition().TotalSeconds-5)<.15,"native seek "+name);
            window.FindControl<Slider>("VolumeSlider")!.Value=.2;await Task.Delay(50);Check(Math.Abs(engine.GetVolume()-.04)<.003,"native gain while paused "+name);
            window.FindControl<Slider>("VolumeSlider")!.Value=0;
            if(!vm.Player.IsLiked)await window.ClickAsync("LikeButton");Check(vm.Player.IsLiked&&LocalMusicRecordStore.IsFavorite(song),"local favorite exact source "+name);
            await window.ClickAsync("PauseButton");await Task.Delay(100);Check(engine.IsPlaying,"native resume "+name);
            // Remove only queue membership before waiting for natural end, to prevent auto-next.
            root.Queue.Clear();
            int ended=0;void OnEnd()=>Interlocked.Increment(ref ended);engine.PlaybackEnded+=OnEnd;
            try
            {
                var duration=vm.Player.TotalDurationSeconds;window.FindControl<Slider>("SeekSlider")!.Value=Math.Max(0,duration-.3);await Task.Delay(1100);
                Check(ended>0,"native BASS end callback "+name);
            }
            finally{engine.PlaybackEnded-=OnEnd;}
            await window.ClickAsync("StopButton");Check(engine.IsStopped,"stop "+name);
        }
        vm.SelectedSong=vm.Library.SelectedPlaylistSongs.First();await window.ClickAsync("PlayButton");await Task.Delay(250);
        var first=engine.ActiveSource;await window.ClickAsync("NextButton");await Task.Delay(250);
        Check(engine.IsPlaying&&engine.ActiveSource!=first,"mixed-format next uses production queue and real stream");
        await window.ClickAsync("PreviousButton");await Task.Delay(250);
        Check(engine.IsPlaying&&engine.ActiveSource==first,"mixed-format previous restores actual stream");await window.ClickAsync("StopButton");
        await vm.History.LoadAsync();vm.Favorites.Refresh();
        Check(vm.History.Songs.Count>=4&&vm.Favorites.Songs.Count>=4,"original local history and favorites persistence");
        Check(paths.Select(OfflineMediaImport.Hash).SequenceEqual(hashes),"all explicit original sample bytes unchanged");
        Check(!Directory.EnumerateFiles(p.Temp).Any(f=>f.EndsWith(".mp3")||f.EndsWith(".flac")||f.EndsWith(".ledger-partial")),"no unfinished copy admitted or left in staging");
        Check(OfflineStartupNetwork.Requests==1,"terminal network has only initial probe");
        OfflineGuiApplication.Audit("FORMAT_COMPLETE","checks="+checks+"; human audio and native file picker pending");window.Close();
    }
    private sealed class CancelOnChunk(CancellationTokenSource cancel):IProgress<long>
    {
        internal long Bytes;
        public void Report(long value){Bytes=value;cancel.Cancel();}
    }
}
