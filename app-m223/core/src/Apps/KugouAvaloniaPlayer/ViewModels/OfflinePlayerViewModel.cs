using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.Startup;
namespace KugouAvaloniaPlayer.ViewModels;

/// <summary>Temporary local navigation adapter. Playback, library and records remain production services.</summary>
public partial class OfflinePlayerViewModel:ObservableObject,IDisposable
{
    public PlayerViewModel Player { get; }
    public LocalMusicLibraryViewModel Library { get; }
    public LocalFavoritesViewModel Favorites { get; }
    public HistoryViewModel History { get; }
    private readonly ILocalMusicLibraryService _library;
    [ObservableProperty] public partial bool IsImporting { get; set; }
    [ObservableProperty] public partial PlaylistItem? SelectedPlaylist { get; set; }
    [ObservableProperty] public partial SongItem? SelectedSong { get; set; }
    [ObservableProperty] public partial SongItem? SelectedHistory { get; set; }
    [ObservableProperty] public partial string PlaylistName { get; set; }="我的测试歌单";
    [ObservableProperty] public partial string Status { get; set; }="受控离线；明确选择 MP3/FLAC/WAV 只读导入副本；不自动扫描或播放。";
    public OfflinePlayerViewModel(PlayerViewModel player,LocalMusicLibraryViewModel library,LocalFavoritesViewModel favorites,HistoryViewModel history,ILocalMusicLibraryService service)
    {Player=player;Library=library;Favorites=favorites;History=history;_library=service;}
    public async Task InitializeAsync()
    {
        await _library.InitializeAsync();
        if (!OfflineStartupProfile.Active) Status = "可添加本地音乐";
        // CORE-02 regression profiles retain the pinned synthetic scan. M1 daily launch must stay empty until an explicit file-picker confirmation.
        if(OfflineStartupProfile.Active&&Environment.GetEnvironmentVariable("M1_LOCAL_DAILY")!="1"&&(await _library.GetPlaylistsAsync()).Count==0)
            await _library.ImportFolderAsync(Path.Combine(OfflineStartupProfile.Current.Media,"scan"));
        await RefreshAsync();await History.LoadAsync();Favorites.Refresh();
    }
    internal async Task ImportSelectedAsync(string[] files,System.Threading.CancellationToken cancel)
    {
        if(IsImporting){Status="导入进行中；可点击取消。";return;}
        if(files.Length>OfflineMediaImport.MaxFiles){Status="每批最多选择 20 个文件；未读取任何源文件。";return;}
        // An empty daily library has no playlist: create one only after the user confirms at least one chosen file.
        if(SelectedPlaylist==null){
            if(cancel.IsCancellationRequested||files.Length==0){Status="已取消导入；没有创建歌单或读取文件。";return;}
            await RefreshAsync();
            if(SelectedPlaylist==null){
                var created=await _library.CreatePlaylistAsync("本地导入");
                await RefreshAsync(created.Id.ToString());
            }
            if(SelectedPlaylist==null)throw new IOException("无法准备本地导入歌单，导入已停止");
        }
        IsImporting=true;
        try
        {
        var id=SelectedPlaylist.Id;var results=new System.Collections.Generic.List<string>();
        foreach(var file in files)
        {
            if(cancel.IsCancellationRequested){results.Add("已取消；已成功导入的歌曲保留");break;}
            try
            {
                var progress=new Progress<long>(bytes=>Status="只读复制 "+Path.GetFileName(file)+" · "+(bytes/1048576d).ToString("F1")+" MiB（可取消）");
                if (!OfflineStartupProfile.Active)
                {
                    await _library.AddFilesToPlaylistAsync(long.Parse(id), new[]{file}, cancel);
                    results.Add(Path.GetFileName(file));
                    continue;
                }
                var result=await Task.Run(()=>OfflineMediaImport.CopySelectedAsync(file,cancel,progress),cancel);
                if(result.Copy!=null)await Task.Run(()=>_library.AddFilesToPlaylistAsync(long.Parse(id),new[]{result.Copy},cancel),cancel);
                results.Add(result.Source+"："+result.Status);
            }
            catch(OperationCanceledException){results.Add("已取消；未完成的副本已清理");break;}
            catch(Exception error){results.Add(Path.GetFileName(file)+"：失败（"+error.Message+"）");}
            Status=string.Join("\n",results);
        }
        await RefreshAsync(id);Status=files.Length==0?"已取消选择；未读取文件。":string.Join("\n",results);
        if(OfflineGuiApplication.Running)OfflineGuiApplication.Audit("IMPORT_BATCH","selected="+files.Length+"; outcomes="+results.Count);
        }
        finally{IsImporting=false;}
    }
    private async Task RefreshAsync(string? id=null)
    {
        await Library.LoadLocalLibraryCommand.ExecuteAsync(null);
        SelectedPlaylist=Library.LocalLibraryPlaylists.FirstOrDefault(p=>p.Id==id)??Library.LocalLibraryPlaylists.FirstOrDefault();
        if(SelectedPlaylist!=null)await Library.OpenPlaylistCommand.ExecuteAsync(SelectedPlaylist);
        SelectedSong=Library.SelectedPlaylistSongs.FirstOrDefault();
    }
    public async Task ActAsync(string action)
    {
        if(IsImporting && action is not ("Stop" or "Pause" or "Mute")){Status="正在导入，请等待或取消后再修改曲库。";return;}
        try
        {
            switch(action)
            {
                case "Open": if(SelectedPlaylist!=null){await Library.OpenPlaylistCommand.ExecuteAsync(SelectedPlaylist);SelectedSong=Library.SelectedPlaylistSongs.FirstOrDefault();}break;
                case "Create": var list=await _library.CreatePlaylistAsync(string.IsNullOrWhiteSpace(PlaylistName)?"测试歌单":PlaylistName.Trim());await RefreshAsync(list.Id.ToString());break;
                case "Delete": if(SelectedPlaylist!=null){await _library.DeletePlaylistAsync(long.Parse(SelectedPlaylist.Id));await RefreshAsync();}break;
                case "Add": if(SelectedPlaylist!=null){await _library.AddFilesToPlaylistAsync(long.Parse(SelectedPlaylist.Id),new[]{Path.Combine(OfflineStartupProfile.Current.Media,"loose","c.wav")});await RefreshAsync(SelectedPlaylist.Id);}break;
                case "Remove": if(SelectedSong!=null)await Library.RemoveSongFromPlaylistCommand.ExecuteAsync(SelectedSong);break;
                case "Play": if(SelectedSong!=null)await Player.PlaySongAsync(SelectedSong,Library.SelectedPlaylistSongs);break;
                case "Pause": ((IPlaybackCommands)Player).TogglePlayPause();break;
                case "Stop": ((IPlaybackCommands)Player).Stop();break;
                case "Previous": await ((IPlaybackCommands)Player).PlayPreviousAsync();break;
                case "Next": await ((IPlaybackCommands)Player).PlayNextAsync();break;
                case "Like": await Player.ToggleLikeCommand.ExecuteAsync(null);Favorites.Refresh();break;
                case "Mute": Player.ToggleMute();break;
                case "PlayFavorite": await Favorites.PlaySelectedCommand.ExecuteAsync(null);break;
                case "RemoveFavorite": Favorites.RemoveSelectedCommand.Execute(null);break;
                case "PlayHistory": if(SelectedHistory!=null)await Player.PlaySongAsync(SelectedHistory,History.Songs);break;
                case "ClearHistory": await History.ClearHistoryCommand.ExecuteAsync(null);break;
                case "Refresh": await RefreshAsync(SelectedPlaylist?.Id);Favorites.Refresh();await History.LoadAsync();break;
            }
            Status="已执行："+action+"；文件和数据仅限隔离 profile。";
        }
        catch(Exception e){Status="操作失败："+e.Message;OfflineGuiApplication.Audit("ACTION_FAILED",action+" "+e.GetType().Name);}
    }
    public void Dispose(){((IPlaybackCommands)Player).Stop();Favorites.Dispose();}
}
