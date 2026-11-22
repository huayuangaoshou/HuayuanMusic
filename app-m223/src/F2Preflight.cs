using System.Net;
using System.Text;
using HuayuanMusic.Provider.Kugou;
using KugouAvaloniaPlayer.Services.Startup;
using ManagedBass;

namespace HuayuanPreview;

internal sealed partial class P2AWorkspace
{
    internal static string? F2Case => Program.Desktop ? null : M219RStartup.Real?"m219r-real":Environment.GetEnvironmentVariable("M208B_F2_PREFLIGHT");
    internal static bool M213Offline => F2Case=="m213-offline";
    internal static bool M215Trace => M213Offline && Environment.GetEnvironmentVariable("M215_OFFLINE_TRACE") is "1" or "close" or "enabled";
    internal static bool M215CloseDuringOpen => M215Trace && Environment.GetEnvironmentVariable("M215_OFFLINE_TRACE")=="close";
    internal static bool M214SimulateSecondOpen => M213Offline &&
        Environment.GetEnvironmentVariable("M214_SIMULATE_SECOND_PROVIDER_OPEN")=="1";
    internal static bool M214InjectReadbackFault => M213Offline &&
        Environment.GetEnvironmentVariable("M214_INJECT_TERMINAL_READBACK_FAULT")=="1";
    internal static bool M213RealOnce => F2LiveStrict &&
        Environment.GetEnvironmentVariable("M213_REAL_HTTP_ONCE")=="1";
    internal static bool M213RowMode => M213Offline||M213RealOnce||M219RStartup.Real;
    internal static bool M211StrictMemberIntent => F2Case=="queue-intent"||M213RowMode;
    internal static bool M210ConsentQueueUi => !Program.Desktop && (M223Normal || F2Case is "queue-consent" or "queue-intent" or "m213-offline");
    internal static bool M209SyntheticQueueUi => F2Case is "queue-ui" or "queue-consent" or "queue-intent" or "m213-offline";
    internal static bool F2Preflight => F2Case is "success" or "failure" or "timeout" or "uia-error" or "http-original" or "queue-ui" or "queue-consent" or "queue-intent" or "m213-offline" or "m219r-real";
    internal static bool F2LiveStrict =>
        Environment.GetEnvironmentVariable("M208B_F2_LIVE_ONCE")=="1" &&
        Environment.GetEnvironmentVariable("M208B_P2BPF_SINGLE")=="1" &&
        Environment.GetEnvironmentVariable("M208B_P2BPF2_MEDIA_SYSTEM_PROXY")=="1" &&
        string.IsNullOrWhiteSpace(F2Case);
    // A fresh on-disk marker must be minted by the external runner AFTER
    // an independent explicit approval. This source and its offline tests
    // intentionally never create a real approval marker.
    // Old F3 and M2-13 use mutually exclusive one-shot markers. A previously
    // consumed F3 marker can NEVER be used to launch an M2-13 process.
    internal static bool F3OriginalHttpStrict =>
        F2LiveStrict &&
        Environment.GetEnvironmentVariable("M208B_F3_HTTP_ONCE")=="1" &&
        Environment.GetEnvironmentVariable("M208B_F3_HTTP_RISK_APPROVED")=="1" &&
        (Environment.GetEnvironmentVariable("M213_REAL_HTTP_ONCE")=="1"
            ?Environment.GetEnvironmentVariable("M213_HTTP_RISK_APPROVED")=="1" &&
             File.Exists(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                 "..","evidence","M213_EXPLICIT_HTTP_AUTHORIZATION_ONCE")))
            :File.Exists(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                 "..","evidence","F3_EXPLICIT_HTTP_AUTHORIZATION_ONCE"))));
    CancellationTokenSource? trialCancellation;
    internal bool CancelTrial()
    {
        if(trialCancellation==null)return false;
        trialCancellation.Cancel();
        return true;
    }
}

internal sealed class F2SyntheticMedia:HttpMessageHandler
{
    int calls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        if(Interlocked.Increment(ref calls)!=1||request.Method!=HttpMethod.Get||
           request.RequestUri!=new Uri(P2AWorkspace.F2Case is "http-original" or "queue-ui" or "queue-consent" or "queue-intent" or "m213-offline"
               ?"http://audio.kugoucdn.com/f2-synthetic.wav"
               :"https://audio.kugoucdn.com/f2-synthetic.wav"))
            throw new UnauthorizedAccessException("Invalid synthetic media dispatch");
        Program.Log("F2_MEDIA_ENTERED",new { calls });
        if(P2AWorkspace.M215CloseDuringOpen||P2AWorkspace.F2Case is "timeout" or "uia-error")
            await Task.Delay(Timeout.InfiniteTimeSpan,ct);
        if(P2AWorkspace.M213Offline&&Environment.GetEnvironmentVariable("M217_OFFLINE_CASE")=="media-fault")
            return new(HttpStatusCode.ServiceUnavailable);
        if(M219RStartup.Active&&Environment.GetEnvironmentVariable("M219R_FAULT")=="media-redirect")
            return new(HttpStatusCode.Redirect){Headers={Location=new Uri("http://redirect.invalid/no-follow")}};
        if(P2AWorkspace.F2Case=="failure")return new(HttpStatusCode.Forbidden);

        return AudioResponse();
    }

    internal static HttpResponseMessage AudioResponse()
    {
        const int rate=22050, samples=rate*8;
        using var data=new MemoryStream();
        using var writer=new BinaryWriter(data,Encoding.ASCII,true);
        writer.Write("RIFF"u8);writer.Write(36+samples*2);writer.Write("WAVEfmt "u8);
        writer.Write(16);writer.Write((short)1);writer.Write((short)1);
        writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);
        writer.Write("data"u8);writer.Write(samples*2);
        for(int i=0;i<samples;i++)writer.Write((short)(2400*Math.Sin(2*Math.PI*330*i/rate)));
        var audio=new ByteArrayContent(data.ToArray());
        audio.Headers.ContentType=new("audio/wav");
        return new(HttpStatusCode.OK){Content=audio};
    }
}

public sealed partial class PreviewWindow
{
    internal async Task PrepareF2PreflightAsync(CorePlaybackAdapter adapter)
    {
        if(string.IsNullOrEmpty(P2AWorkspace.F2Case))return;
        if(!(P2AWorkspace.F2Preflight||P2AWorkspace.M212SnapshotPreview||P2AWorkspace.F2Case=="restore")||
           !Path.GetFileName(OfflineStartupProfile.Current.Root).StartsWith("p17-body-r3-manual-test-r2-f2-",StringComparison.Ordinal)||
           R2Model==null)
            throw new UnauthorizedAccessException("F2 fixture profile required");
        if(P2AWorkspace.F2Preflight||P2AWorkspace.M212SnapshotPreview)
        {
            if(adapter.State.QueueEntries.Length!=0||adapter.State.Playing)throw new InvalidOperationException("Fresh F2 profile required");
            await adapter.VolumeAsync(0.35);
            if(P2AWorkspace.M215Trace)
            {
                adapter.EnableProviderLoadTrace(Environment.GetEnvironmentVariable("M215_OFFLINE_TRACE")=="enabled");
                await adapter.SetModeAsync((int)KugouAvaloniaPlayer.Models.PlayMode.Normal);
                LogF2PreflightState(adapter,"F2_PREFLIGHT_READY");
                return;
            }
            var id=adapter.State.Tracks[0].Id;
            await adapter.QueueAsync("append",id);
            await adapter.QueueAsync("append",id);
            if(adapter.State.Error!=""||adapter.State.QueueEntries.Length!=2)throw new InvalidOperationException("F2 seed queue failed");
        }
        LogF2PreflightState(adapter,"F2_PREFLIGHT_READY");
    }

    internal static int F2BassDevices()
    {
        int devices=0;
        for(int i=1;Bass.GetDeviceInfo(i,out var device);i++)if(device.IsInitialized)devices++;
        return devices;
    }

    internal void LogF2PreflightState(CorePlaybackAdapter adapter,string kind)
    {
        if(string.IsNullOrEmpty(P2AWorkspace.F2Case))return;
        Program.Log(kind,new {
            queue=adapter.State.QueueEntries.Select(e=>e.EntryId),
            volume=adapter.State.Volume,
            playing=adapter.State.Playing||adapter.State.Paused,
            session=adapter.Kugou.Account!=null||adapter.Kugou.LoggedIn||adapter.Kugou.Enabled,
            streams=adapter.ProviderStreamCount,
            oneCore=ReferenceEquals(R2Model?.Player.State,adapter.State),
            devices=F2BassDevices(),
            startupMockRequests=OfflineStartupNetwork.Requests
        });
    }
}


