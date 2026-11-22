using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Avalonia.Threading;
using KugouAvaloniaPlayer.ViewModels;
using ManagedBass;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using SimpleAudio;
namespace KugouAvaloniaPlayer.Services.Startup;

/// <summary>Real Program + real production Pure.DI infrastructure root; no App/VM/window admission.</summary>
internal static class OfflineInfrastructureProbe
{
    internal static int Run(string name,string ticket,TextWriter output)
    {
        OfflineStartupProfile? scope=null;
        try
        {
            scope=OfflineStartupProfile.Install(name,ticket);
            RunAsync(scope,output).GetAwaiter().GetResult();
            return 0;
        }
        catch(Exception e) { output.WriteLine("R2_INFRASTRUCTURE_FAILED; "+e.GetType().Name+"; GUI_AUTHORIZED=false"); if(scope!=null)File.AppendAllText(Path.Combine(scope.Root,"failure.txt"),e.ToString());return 2; }
        finally { try { Log.CloseAndFlush();if(scope!=null)SimpleAudioPlayer.Free(); } finally { scope?.Dispose(); } }
    }
    private static async Task RunAsync(OfflineStartupProfile scope,TextWriter output)
    {
        int checks=0;
        void Check(bool ok,string label){output.WriteLine((ok?"PASS ":"FAIL ")+label);if(!ok)throw new InvalidOperationException(label);checks++;}
        Check(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)==scope.Temp&&AudioCacheLocation.CurrentLocalRoot==scope.Local&&AppDataPaths.RoamingRoot==scope.Roaming&&AppDataPaths.LocalRoot==scope.Local,"profile selected before data services");
        SettingsManager.Load();CrashReporting.ConfigureLogging();
        Check(!SettingsManager.Settings.AutoCheckUpdate&&SettingsManager.Settings.MusicVolume==0,"safe settings skip legacy database and JSON");
        var before=OfflineStartupNetwork.Requests;
        using(var provider=new AvaloniaAppServiceProvider())
        using(var owned=provider.CreateOfflineInfrastructureRoot(NullLoggerFactory.Instance,Dispatcher.CurrentDispatcher))
        {
            var root=owned.Value;
            if(scope.Fault)throw new InvalidOperationException("Authorized injected infrastructure failure");
            Check(Avalonia.Application.Current==null&&OfflineStartupNetwork.Requests==before,"production infrastructure constructors start no App or HTTP work");
            Check(root.Session.Session.Token==""&&root.Session.Session.UserId=="0"&&System.Linq.Enumerable.All(root.Cookies.GetAllCookies().CastCookies(), c=>c.Value==""||c.Value=="0"),"memory session and fresh cookies are guest only");
            Check(AppDatabase.DatabasePath.StartsWith(scope.Roaming+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"production SQLite path confined to profile");
            await root.Login.InitializeLocalSessionAsync();await root.Login.LoadCurrentUserProfileAsync();await root.Vip.TryEnsureDailyVipAsync();Check(!await root.Network.IsInternetAvailableAsync(),"offline services deny connectivity without probing");
            Check(OfflineStartupNetwork.Requests==before,"login profile VIP and connectivity operations invoke no transport");
            using(var response=await root.HttpClient.GetAsync("https://example.invalid/di"))Check(response.StatusCode==HttpStatusCode.ServiceUnavailable,"DI terminal Mock");
            using(var client=root.Clients.CreateClient("r2"))using(var response=await client.GetAsync("http://127.0.0.1:1/factory"))Check(response.StatusCode==HttpStatusCode.ServiceUnavailable,"production named HTTP factory terminal Mock");
            using(var client=OfflineStartupNetwork.ImageClient())using(var response=await client.GetAsync("https://example.invalid/image"))Check(response.StatusCode==HttpStatusCode.ServiceUnavailable,"production image client terminal Mock");
            using(var client=OfflineStartupNetwork.FallbackClient())using(var response=await client.GetAsync("https://example.invalid/default-image"))Check(response.StatusCode==HttpStatusCode.ServiceUnavailable,"default image-loader fallback terminal Mock");
            using(var client=OfflineStartupNetwork.AudioClient())using(var response=await client.GetAsync("https://example.invalid/audio"))Check(response.StatusCode==HttpStatusCode.ServiceUnavailable,"production audio transport terminal Mock");
            Check(await root.UserClient.GetUserInfoAsync()==null&&OfflineStartupNetwork.Requests==before+5,"real SDK guest account guard does not call transport");
            try { await root.Transport.SendAsync(new KuGou.Net.Protocol.Transport.KgRequest{Path="/r2-probe",BaseUrl="https://example.invalid",Method=System.Net.Http.HttpMethod.Post}); } catch(System.Net.Http.HttpRequestException) { }
            Check(OfflineStartupNetwork.Requests==before+6,"real SDK transport shares production terminal Mock");
            Check(await root.Picker.PickSingleFolderAsync("r2")==null&&await root.Picker.PickSingleImageFileAsync("r2")==null&&(await root.Picker.PickAudioFilesAsync("r2")).Count==0,"production pickers do not open arbitrary filesystem UI");
            var source=Path.Combine(scope.Media,"scan","a.wav");
            Check((await root.Sources.ResolveAsync(new SongItem{LocalFilePath=source,LocalSourceType="LocalFile"},"128",default)).Success,"production resolver accepts admitted synthetic WAV");
            foreach(var path in new[]{"https://example.invalid/a.wav",Path.Combine(scope.Root,"outside.wav"),@"\\localhost\share\a.wav",Path.Combine(scope.Media,"..","outside.wav")})
            {
                Check(!await root.Coordinator.LoadAsync(path,"denied",1,TimeSpan.FromSeconds(1),default)&&!await root.Coordinator.PrepareNextAsync(path,"denied",1,TimeSpan.FromSeconds(1),default),"production load and preload reject nonfixture sources before device");
            }
            await root.Library.InitializeAsync();var list=await root.Library.ImportFolderAsync(Path.Combine(scope.Media,"scan"));
            Check((await root.Library.GetPlaylistTracksAsync(list.Id)).Count==2,"original SQLite library imports two authored tracks");
            bool outsideDenied=false;try{await root.Library.ImportFolderAsync(scope.Root);}catch(IOException){outsideDenied=true;}Check(outsideDenied,"production library rejects outside-media folder before scan");
            var song=new SongItem{LocalFilePath=source,LocalSourceType="LocalFile",Name="R2 Synthetic A",Singer="Huayuan Synth"};
            LocalMusicRecordStore.SetFavorite(song,true);LocalMusicRecordStore.SetFavorite(song,true);await root.History.RecordPlayedAsync(song);
            Check(root.Favorites.IsLiked(song)&&LocalMusicRecordStore.LoadFavorites().Count==1&&(await root.History.LoadSongsAsync()).Count==1,"production local favorite history and repeated records remain independent");
            await root.Lyrics.LoadLocalLyricsAsync(source);Check(root.Lyrics.LyricLines.Count>0,"original local LRC parser and profile cache reused");
            Check(new[]{("session","default"),("settings","app"),("favorite_like_cache","0"),("playback_history","0")}.AllSentinelsPreserved(),"synthetic legacy SQLite values are not migrated or overwritten");
            Check(File.ReadAllText(Path.Combine(scope.Roaming,"session.json"))=="SYNTHETIC_KEEP"&&File.ReadAllText(Path.Combine(scope.Roaming,"AvaloniaPlayerSettings.json"))=="SYNTHETIC_KEEP","synthetic legacy account/settings files untouched");
            bool immutable=false;try{OfflineStartupProfile.Install(name:"other-profile",ticket:"00000000000000000000000000000000");}catch(InvalidOperationException){immutable=true;}Check(immutable,"second profile cannot be installed in same process");
            Check(Bass.CurrentDevice==-1&&root.Coordinator.Player.IsStopped,"no implicit audio device initialization");
            bool guiDenied=false;try{NormalStartupGate.DemandGuiAuthorization();}catch(InvalidOperationException){guiDenied=true;}Check(guiDenied,"GUI admission remains refused after infrastructure success");
        }
        Check(Bass.CurrentDevice==-1&&!AnyDeviceInitialized(),"production root disposal leaves no initialized device");
        scope.ValidateTree(scope.Root);
        output.WriteLine("R2_INFRASTRUCTURE_PASS checks="+checks+"; GUI_AUTHORIZED=false; NORMAL_APP_RUNTIME=BLOCKED");
    }
    private static bool AnyDeviceInitialized() { for(int i=1;Bass.GetDeviceInfo(i,out var info);i++)if(info.IsInitialized)return true;return false; }
    private static System.Collections.Generic.IEnumerable<Cookie> CastCookies(this CookieCollection cookies) { foreach(Cookie c in cookies)yield return c; }
    private static bool AllSentinelsPreserved(this (string Scope,string Key)[] pairs)
    { foreach(var p in pairs)if(AppSqliteStore.LoadValue(p.Scope,p.Key)!="SYNTHETIC_KEEP")return false;return true; }
}
