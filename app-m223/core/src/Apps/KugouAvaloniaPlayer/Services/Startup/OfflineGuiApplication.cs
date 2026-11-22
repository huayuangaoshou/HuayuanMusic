using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using KugouAvaloniaPlayer.ViewModels;
using KugouAvaloniaPlayer.Views;
using ManagedBass;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using SimpleAudio;
namespace KugouAvaloniaPlayer.Services.Startup;

internal static class OfflineGuiApplication
{
    internal static bool Running { get; private set; }
    internal static bool SelfTest { get; private set; }
    private static int _result;
    private static readonly object AuditGate=new();
    internal static void Audit(string kind,string detail)
    {
        lock(AuditGate)File.AppendAllText(Path.Combine(OfflineStartupProfile.Current.Root,"gui-events.tsv"),$"{DateTimeOffset.UtcNow:O}\t{kind}\t{detail.Replace('\n',' ').Replace('\r',' ')}\n");
    }
    internal static int Run(string name,string ticket,bool selftest,TextWriter output,Func<AppBuilder> builder)
    {
        OfflineStartupProfile? scope=null;
        try
        {
            scope=OfflineStartupProfile.Install(name,ticket);
            if(!scope.LiveGuiPermit)throw new InvalidOperationException("Profile does not authorize this window launch");
            using(var client=OfflineStartupNetwork.MockClient())
            using(var response=client.GetAsync("https://example.invalid/before-gui").GetAwaiter().GetResult())
                if(response.StatusCode!=HttpStatusCode.ServiceUnavailable)throw new InvalidOperationException("Terminal network preflight failed");
            SettingsManager.Load();SettingsManager.Settings.MusicVolume=selftest?0:.35f;
            CrashReporting.ConfigureLogging();
            using var permission=NormalStartupGate.GrantOfflineGui(()=>scope.LiveGuiPermit);
            SelfTest=selftest;Running=true;Audit("PROGRAM_ADMITTED","profile/media/lease/memory/network ready before App; selftest="+selftest);
            builder().StartWithClassicDesktopLifetime(Array.Empty<string>());
            Audit("PROGRAM_EXIT","result="+_result);
            output.WriteLine("P15_WINDOW_EXIT result="+_result+"; unrestricted startup remains refused");return _result;
        }
        catch(Exception e)
        {
            _result=2;
            if(scope!=null)Audit("FATAL",e.ToString());
            output.WriteLine("P15_WINDOW_REFUSED_OR_FAILED; "+e.GetType().Name);return 2;
        }
        finally
        {
            Running=false;
            try{Log.CloseAndFlush();if(scope!=null)SimpleAudioPlayer.Free();}finally{scope?.Dispose();}
        }
    }
    internal static void InitializeDesktop(Application app)
    {
        NormalStartupGate.DemandGuiAuthorization();
        if(app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)throw new InvalidOperationException("Desktop lifetime required");
        Audit("APP_FRAMEWORK","real App framework initialization");
        var provider=new AvaloniaAppServiceProvider();
        var owned=provider.CreateOfflineWindowRoot(NullLoggerFactory.Instance,Dispatcher.CurrentDispatcher);
        var root=owned.Value;var vm=root.ViewModel;
        var window=new OfflinePlayerWindow{DataContext=vm};desktop.MainWindow=window;
        bool canClose=false,closing=false;
        window.Closing+=async (_,e)=>
        {
            if(canClose)return;e.Cancel=true;if(closing)return;closing=true;window.IsEnabled=false;
            try{window.CancelPendingImport();await window.PendingOperations;vm.Dispose();}
            catch(Exception error){_result=2;Audit("CLOSE_ERROR",error.ToString());}
            finally{canClose=true;window.Close();}
        };
        desktop.Exit+=(_,_)=>
        {
            try{vm.Dispose();owned.Dispose();provider.Dispose();}
            catch(Exception e){_result=2;Audit("DISPOSE_ERROR",e.ToString());}
            finally
            {
                SimpleAudioPlayer.Free();bool remaining=false;for(int i=1;Bass.GetDeviceInfo(i,out var d);i++)if(d.IsInitialized)remaining=true;
                Audit("DEVICE_RELEASE","current="+Bass.CurrentDevice+"; remaining="+remaining);if(remaining)_result=2;
            }
        };
        window.Opened+=async (_,_)=>
        {
            try
            {
                await vm.InitializeAsync();window.SetReady();await Task.Delay(1000);window.UpdateLayout();
                Audit("WINDOW_OPENED","native="+window.TryGetPlatformHandle()?.Handle+"; visible="+window.IsVisible+"; dpi="+window.RenderScaling);
                Audit("PROFILE_RESTORED","playlists="+vm.Library.LocalLibraryPlaylists.Count+"; favorites="+vm.Favorites.Songs.Count+"; history="+vm.History.Songs.Count);
                if(OfflineStartupProfile.Current.Fault)throw new InvalidOperationException("Injected failure after real window opened");
                if(SelfTest){if(OfflineStartupProfile.Current.Import)await OfflineMediaImportExercise.RunAsync(window,root);else await ExerciseAsync(window,root);}
            }
            catch(Exception e){_result=2;Audit("SELFTEST_FAIL",e.ToString());window.Close();}
        };
        Dispatcher.UIThread.UnhandledException+=(_,e)=>{_result=2;Audit("UI_EXCEPTION",e.Exception.ToString());e.Handled=true;window.Close();};
    }
    private static async Task ExerciseAsync(OfflinePlayerWindow window,OfflineWindowRoot root)
    {
        var vm=root.ViewModel;var player=root.Player;var engine=root.Coordinator.Player;int checks=0;
        void Check(bool ok,string name){Audit(ok?"CHECK_PASS":"CHECK_FAIL",name);if(!ok)throw new InvalidOperationException(name);checks++;}
        T Control<T>(string name) where T:Control=>window.FindControl<T>(name)??throw new InvalidOperationException(name);
        Check(window.IsVisible&&window.TryGetPlatformHandle()?.Handle is { } hwnd&&hwnd!=IntPtr.Zero&&Application.Current is App,"real Program App DI native window created");
        Check(!player.IsPlayingAudio&&engine.IsStopped&&Bass.CurrentDevice==-1&&root.Queue.PlaybackQueue.Count==0,"no autoplay device or historical queue restore");
        Check(OfflineStartupNetwork.Requests==1,"no online constructors or startup requests beyond pre-GUI terminal probe");
        Check(Math.Abs(window.RenderScaling-1.5)<.01,"actual window at 150 percent DPI");
        var play=Control<Button>("PlayButton");var stop=Control<Button>("StopButton");
        Check(play.IsVisible&&stop.IsVisible&&play.Bounds.Height>=32&&stop.Bounds.Height>=32,"local playback controls have laid-out visible hit areas");
        Control<TextBox>("PlaylistNameBox").Text="自动验收歌单";
        await window.ClickAsync("CreateButton");Check(vm.Library.SelectedPlaylistSongs.Count==0,"create local playlist through actual button route");
        await window.ClickAsync("AddButton");Check(vm.Library.SelectedPlaylistSongs.Count==1,"add fixed synthetic C through actual button route");
        Control<ListBox>("SongList").SelectedIndex=0;
        await window.ClickAsync("RemoveButton");Check(vm.Library.SelectedPlaylistSongs.Count==0,"remove library membership preserves file");
        await window.ClickAsync("AddButton");
        var scan=vm.Library.LocalLibraryPlaylists.First(p=>p.Name=="scan");vm.SelectedPlaylist=scan;await vm.ActAsync("Open");
        Control<ListBox>("SongList").SelectedIndex=0;
        await window.ClickAsync("PlayButton");await Task.Delay(600);var before=engine.GetPosition().TotalSeconds;await Task.Delay(350);
        Check(engine.IsPlaying&&player.IsPlayingAudio&&engine.GetPosition().TotalSeconds>before+.15&&engine.GetActiveAnalysisSnapshot().Rms>0,"actual production decoded audio and native clock; muted automation");
        Check(player.MusicVolume==0&&player.LyricLines.Count>0&&player.CurrentLyricLine!=null,"muted test and production synchronized LRC");
        await window.ClickAsync("PauseButton");before=engine.GetPosition().TotalSeconds;await Task.Delay(300);
        Check(engine.IsPaused&&Math.Abs(engine.GetPosition().TotalSeconds-before)<.06,"actual pause freezes audio clock");
        Control<Slider>("SeekSlider").Value=6.25;await Task.Delay(100);Check(Math.Abs(engine.GetPosition().TotalSeconds-6.25)<.15,"actual seek slider binding reaches native stream");
        Control<Slider>("VolumeSlider").Value=.25;await Task.Delay(60);Check(Math.Abs(player.MusicVolume-.25)<.001&&Math.Abs(engine.GetVolume()-.0625)<.002,"actual volume slider reaches engine while paused");
        await window.ClickAsync("MuteButton");Check(player.IsMuted&&engine.GetVolume()==0,"actual mute button");
        await window.ClickAsync("MuteButton");Check(!player.IsMuted,"unmute restores volume while paused");Control<Slider>("VolumeSlider").Value=0;
        if(!player.IsLiked)await window.ClickAsync("LikeButton");
        Control<TabControl>("Pages").SelectedIndex=1;vm.Favorites.Refresh();vm.Favorites.SelectedSong=vm.Favorites.Songs.First(s=>s.LocalFilePath==player.CurrentPlayingSong?.LocalFilePath);
        await window.ClickAsync("RemoveFavoriteButton");Check(!player.IsLiked,"favorite page removal updates production heart");
        await window.ClickAsync("LikeButton");Check(player.IsLiked&&vm.Favorites.Songs.Count>0,"local favorite button and independent storage");
        Control<TabControl>("Pages").SelectedIndex=0;
        await window.ClickAsync("PauseButton");await Task.Delay(200);Check(engine.IsPlaying,"resume uses original production command");
        var old=player.CurrentPlayingSong?.LocalFilePath;await window.ClickAsync("NextButton");await Task.Delay(150);Check(player.CurrentPlayingSong?.LocalFilePath!=old,"next track from production queue");
        await window.ClickAsync("PreviousButton");await Task.Delay(150);Check(player.CurrentPlayingSong?.LocalFilePath==old,"previous track from production queue");
        await window.ClickAsync("StopButton");Check(engine.IsStopped&&!player.IsPlayingAudio,"stop resets real playback state");
        Control<TabControl>("Pages").SelectedIndex=2;await window.ClickAsync("ClearHistoryButton");await Task.Delay(100);Check(vm.History.Songs.Count==0&&vm.Favorites.Songs.Count>0,"history clear preserves favorites");
        Control<TabControl>("Pages").SelectedIndex=0;Control<ListBox>("SongList").SelectedIndex=0;await window.ClickAsync("PlayButton");await Task.Delay(250);await vm.History.LoadAsync();Check(vm.History.Songs.Count>0,"new successful playback records production history");
        await window.ClickAsync("StopButton");Control<TabControl>("Pages").SelectedIndex=1;vm.Favorites.Refresh();vm.Favorites.SelectedSong=vm.Favorites.Songs.First();
        await window.ClickAsync("PlayFavoriteButton");await Task.Delay(150);Check(engine.IsPlaying,"actual favorites page button uses production playback");
        await window.ClickAsync("StopButton");Control<TabControl>("Pages").SelectedIndex=2;await vm.History.LoadAsync();vm.SelectedHistory=vm.History.Songs.First();
        await window.ClickAsync("PlayHistoryButton");await Task.Delay(150);Check(engine.IsPlaying,"actual history page button uses production playback");
        Control<TabControl>("Pages").SelectedIndex=3;window.UpdateLayout();Check(Control<TabControl>("Pages").SelectedIndex==3,"actual lyric tab selected");
        Check(OfflineStartupNetwork.Requests==1,"all interactive local operations invoke no HTTP");
        try{using var image=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height),new Vector(96,96));image.Render(window);image.Save(Path.Combine(OfflineStartupProfile.Current.Root,"window-APP-RENDER.png"),PngBitmapEncoderOptions.Default);Audit("APP_RENDER","offscreen application render, not OS screenshot");}catch(Exception e){Audit("APP_RENDER_PENDING",e.GetType().Name);}
        Audit("SELFTEST_COMPLETE","checks="+checks+"; real window routed clicks and sliders; human pointer/audio pending");
        window.Close();
    }
}
