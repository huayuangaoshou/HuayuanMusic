using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Services;

namespace KugouAvaloniaPlayer.ViewModels;
public partial class LocalFavoritesViewModel : PageViewModelBase, IDisposable
{
    private readonly IPlaybackCommands _playback;
    public override string DisplayName => "本地收藏";
    public override string Icon => "/Assets/music-folder-8-svgrepo-com.svg";
    public AvaloniaList<SongItem> Songs { get; } = new();
    [ObservableProperty] public partial SongItem? SelectedSong { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "本地独立收藏；源文件失踪时仍保留，可取消收藏。";
    public LocalFavoritesViewModel(IPlaybackCommands playback)
    {
        _playback=playback;LocalMusicRecordStore.FavoritesChanged+=OnChanged;Refresh();
    }
    public void Refresh()
    {
        try{var keep=SelectedSong?.LocalFilePath;Songs.Clear();Songs.AddRange(LocalMusicRecordStore.LoadFavorites());SelectedSong=System.Linq.Enumerable.FirstOrDefault(Songs,s=>s.LocalFilePath==keep);}
        catch(Exception ex){Status="本地收藏读取失败，数据保留："+ex.Message;}
    }
    private void OnChanged(){if(Dispatcher.UIThread.CheckAccess())Refresh();else Dispatcher.UIThread.Post(Refresh);}
    [RelayCommand] private async Task PlaySelected()
    {
        if(SelectedSong is not { } song){Status="请先选择一首本地收藏。";return;}
        if(!File.Exists(song.LocalFilePath)){Status="源文件已失踪；收藏记录保留，可取消收藏。";return;}
        await _playback.PlayAsync(song,Songs);
    }
    [RelayCommand] private void RemoveSelected()
    {
        if(SelectedSong is not { } song){Status="请先选择一首本地收藏。";return;}
        try{LocalMusicRecordStore.SetFavorite(song,false);Status="已取消本地收藏；源文件和历史保留。";}catch(Exception ex){Status="取消失败，数据保留："+ex.Message;}
    }
    public void Dispose()=>LocalMusicRecordStore.FavoritesChanged-=OnChanged;
}
