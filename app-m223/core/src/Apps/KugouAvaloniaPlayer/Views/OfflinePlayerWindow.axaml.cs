using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Linq;
using System.Threading;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using KugouAvaloniaPlayer.ViewModels;
using KugouAvaloniaPlayer.Services.Startup;
namespace KugouAvaloniaPlayer.Views;
public partial class OfflinePlayerWindow:Window
{
    internal Task LastAction { get; private set; }=Task.CompletedTask;
    private Task _importTask=Task.CompletedTask;
    internal Task PendingOperations=>Task.WhenAll(LastAction,_importTask);
    private bool _ready;
    private CancellationTokenSource? _importCancel;
    private async void ImportClick(object? sender,RoutedEventArgs e)
    {
        if(!_ready||_importCancel!=null||DataContext is not OfflinePlayerViewModel vm)return;
        if(!OfflineStartupProfile.Current.Import){vm.Status="此旧版许可仅允许合成 WAV；请使用 P1.6 安全入口。";return;}
        _importTask=ImportAsync(vm);LastAction=_importTask;await _importTask;
    }
    private async Task ImportAsync(OfflinePlayerViewModel vm)
    {
        _importCancel=new CancellationTokenSource();
        try
        {
            var start=await StorageProvider.TryGetFolderFromPathAsync(new Uri(OfflineStartupProfile.Current.Media))??throw new InvalidOperationException("无法定位隔离媒体目录，拒绝打开默认目录");
            using(start)
            {
            var selected=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="明确选择本地音频（只读复制，每批最多20首）",SuggestedStartLocation=start,AllowMultiple=true,FileTypeFilter=new[]{new FilePickerFileType("本地音频"){Patterns=new[]{"*.mp3","*.flac","*.wav"}}}});
            try
            {
                var paths=selected.Select(f=>f.TryGetLocalPath()??throw new InvalidOperationException("拒绝非本地文件")).ToArray();
                await vm.ImportSelectedAsync(paths,_importCancel.Token);
            }
            finally{foreach(var file in selected)file.Dispose();}
            }
        }
        catch(Exception error){vm.Status="导入失败："+error.Message;}
        finally{_importCancel.Dispose();_importCancel=null;}
    }
    internal void CancelPendingImport()=>_importCancel?.Cancel();
    private void CancelImport(object? sender,RoutedEventArgs e)=>CancelPendingImport();
    public OfflinePlayerWindow(){AvaloniaXamlLoader.Load(this);}
    internal void SetReady()=>_ready=true;
    private async void ActionClick(object? sender,RoutedEventArgs e)
    {
        if(!_ready || DataContext is not OfflinePlayerViewModel vm || sender is not Button b)return;
        LastAction=vm.ActAsync(b.Tag?.ToString()??"");await LastAction;
    }
    private async void PlaylistChanged(object? sender,SelectionChangedEventArgs e)
    {
        if(_ready&&DataContext is OfflinePlayerViewModel vm&&sender is ListBox list&&list.SelectedItem is PlaylistItem selected){vm.SelectedPlaylist=selected;LastAction=vm.ActAsync("Open");await LastAction;}
    }
    internal async Task ClickAsync(string name)
    {
        var button=this.FindControl<Button>(name)??throw new InvalidOperationException(name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await LastAction;
        OfflineGuiApplication.Audit("ROUTED_CLICK",name);
    }
}
