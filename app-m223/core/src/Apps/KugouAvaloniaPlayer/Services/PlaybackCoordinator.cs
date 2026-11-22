using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KuGou.Net.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using SimpleAudio;

namespace KugouAvaloniaPlayer.Services;

public interface IPlaybackCoordinator : IDisposable
{
    DualTrackAudioPlayer Player { get; }
    Task<bool> LoadAsync(string source, string songName, float normalizationGain, TimeSpan timeout,
        CancellationToken cancellationToken);
    Task<bool> PrepareNextAsync(string source, string songName, float normalizationGain, TimeSpan timeout,
        CancellationToken cancellationToken);
    void InvalidatePendingLoads();
}

public sealed class PlaybackCoordinator(ILogger<PlaybackCoordinator> logger) : IPlaybackCoordinator
{
    private readonly SemaphoreSlim _streamLoadGate = new(1, 1);
    private readonly HttpClient _remoteAudioClient = Startup.OfflineStartupNetwork.AudioClient();
    private int _streamLoadOperationVersion;
    internal Action<int, string, string>? ProviderLoadTrace { get; set; }

    public DualTrackAudioPlayer Player { get; } = new();

    public async Task<bool> LoadAsync(
        string source,
        string songName,
        float normalizationGain,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await RunLoadAsync(
            source,
            songName,
            normalizationGain,
            timeout,
            prepareOnly: false,
            cancellationToken);
    }

    public async Task<bool> PrepareNextAsync(
        string source,
        string songName,
        float normalizationGain,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await RunLoadAsync(
            source,
            songName,
            normalizationGain,
            timeout,
            prepareOnly: true,
            cancellationToken);
    }

    private async Task<bool> RunLoadAsync(
        string source,
        string songName,
        float normalizationGain,
        TimeSpan timeout,
        bool prepareOnly,
        CancellationToken cancellationToken)
    {
        if (Startup.OfflineStartupProfile.Active && !Startup.OfflineStartupProfile.Current.AllowsMedia(source) && !ProviderStreamRegistry.Allows(source)) return false;

        bool providerSource = ProviderStreamRegistry.IsProvider(source);
        if (providerSource && prepareOnly && !ProviderStreamRegistry.AllowsPreparation(source))
            return false;

        var operationVersion = Interlocked.Increment(ref _streamLoadOperationVersion);
        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationCts.CancelAfter(timeout);
        try
        {
            var loadTask = Task.Run(async () =>
            {
                await _streamLoadGate.WaitAsync(operationCts.Token).ConfigureAwait(false);
                try
                {
                    if (operationVersion != Volatile.Read(ref _streamLoadOperationVersion) ||
                        operationCts.IsCancellationRequested)
                        return false;

                    var loaded = await LoadPlayerSourceAsync(
                        source,
                        normalizationGain,
                        prepareOnly,
                        operationCts.Token, operationVersion).ConfigureAwait(false);
                    if (!loaded)
                        return false;

                    if (operationVersion != Volatile.Read(ref _streamLoadOperationVersion) ||
                        operationCts.IsCancellationRequested)
                    {
                        if (prepareOnly)
                            Player.CancelPrepared();
                        else
                            Player.Stop();
                        return false;
                    }

                    return true;
                }
                finally
                {
                    _streamLoadGate.Release();
                }
            }, CancellationToken.None);

            var completed = await Task.WhenAny(loadTask, Task.Delay(timeout, cancellationToken));
            if (completed != loadTask)
            {
                operationCts.Cancel();
                // Provider open owns the stream/grant until its worker unwinds.
                // Do not let Close dispose the Core/BASS while that worker still runs.
                if (providerSource)
                {
                    try { await loadTask; }
                    catch (OperationCanceledException) when (operationCts.IsCancellationRequested) { }
                }
                if (cancellationToken.IsCancellationRequested) return false;
                Interlocked.CompareExchange(ref _streamLoadOperationVersion, operationVersion + 1, operationVersion);
                logger.LogWarning(
                    prepareOnly ? "预加载歌曲超时: {SongName}, timeout={Timeout}s" : "加载歌曲超时: {SongName}, timeout={Timeout}s",
                    songName,
                    timeout.TotalSeconds);
                return false;
            }

            if (cancellationToken.IsCancellationRequested ||
                operationVersion != Volatile.Read(ref _streamLoadOperationVersion))
                return false;

            return await loadTask;
        }
        catch (OperationCanceledException)
        {
            Interlocked.CompareExchange(ref _streamLoadOperationVersion, operationVersion + 1, operationVersion);
            return false;
        }
    }

    private async Task<bool> LoadPlayerSourceAsync(
        string source,
        float normalizationGain,
        bool prepareOnly,
        CancellationToken cancellationToken, int operationVersion)
    {
        if (ProviderStreamRegistry.IsProvider(source))
        {
            // Never route opaque provider handles to native URL loading or the HTTP fallback.
            var origin = prepareOnly ? "prepare" : "load";
            ProviderLoadTrace?.Invoke(operationVersion, origin, "open");
            ProviderStreamRegistry.Input input;
            try { input = await ProviderStreamRegistry.OpenAsync(source, cancellationToken); }
            catch { ProviderLoadTrace?.Invoke(operationVersion, origin, "rejected"); throw; }
            var accepted = false;
            try
            {
                ProviderLoadTrace?.Invoke(operationVersion, origin, "opened");
                cancellationToken.ThrowIfCancellationRequested();
                accepted = prepareOnly
                    ? Player.PrepareNext(input.Stream, input.Length, source, input.Owner, normalizationGain)
                    : Player.Load(input.Stream, input.Length, source, input.Owner, normalizationGain);
                return accepted;
            }
            finally { if (!accepted) { input.Stream.Dispose(); input.Owner.Dispose(); } }
        }
        if (!IsHttpUrl(source))
            return prepareOnly
                ? Player.PrepareNext(source, normalizationGain)
                : Player.Load(source, normalizationGain);

        HttpResponseMessage? response = null;
        try
        {
            response = await _remoteAudioClient.GetAsync(
                source,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var inputStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var contentLength = response.Content.Headers.ContentLength ?? 0;
            var loaded = prepareOnly
                ? Player.PrepareNext(
                    inputStream,
                    contentLength,
                    source,
                    response,
                    normalizationGain)
                : Player.Load(
                    inputStream,
                    contentLength,
                    source,
                    response,
                    normalizationGain);

            if (loaded)
            {
                response = null;
                return true;
            }

            logger.LogWarning(
                ".NET 音频流无法交给 BASS，回退 BASS 直连: {Source}, Detail={Detail}",
                source,
                Player.LastErrorDetail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            logger.LogWarning(ex, ".NET 音频流建立失败，回退 BASS 直连: {Source}", source);
        }
        finally
        {
            response?.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return prepareOnly
            ? Player.PrepareNext(source, normalizationGain)
            : Player.Load(source, normalizationGain);
    }

    private static bool IsHttpUrl(string source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public void InvalidatePendingLoads()
    {
        Interlocked.Increment(ref _streamLoadOperationVersion);
    }

    public void Dispose()
    {
        InvalidatePendingLoads();
        _streamLoadGate.Dispose();
        _remoteAudioClient.Dispose();
        Player.Dispose();
    }
}
