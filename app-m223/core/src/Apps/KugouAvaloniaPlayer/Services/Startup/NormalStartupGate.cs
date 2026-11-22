using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace KugouAvaloniaPlayer.Services.Startup;

/// <summary>
/// P1.5A admission fence, not an offline replacement for the production composition.
/// No argument authorizes App/DI while their isolation audit remains BLOCKED.
/// This type must not depend on settings, logging, session, database or Avalonia types.
/// </summary>
internal static class NormalStartupGate
{
    internal const int Refused = 78;
    internal const int Failed = 2;
    internal const string Marker = "HUAYUAN_P15A_PREFLIGHT_ONLY_V1";
    private const string Workspace = @"D:\AI\Projects\HuayuanMusic";

    internal static int Run(string[] args, TextWriter output) => RunInWorkspace(args, Workspace, output);

    // Extends the existing admission fence for a headless infrastructure probe only.
    // Callback admission is not GUI authorization; DemandGuiAuthorization stays unchanged.
    internal static int RunInfrastructure(string[] args, TextWriter output, Func<string,string,TextWriter,int> probe)
    {
        if(args.Length!=3 || args[0]!="--p15a-r2-infrastructure" || !args[1].StartsWith("--profile=",StringComparison.Ordinal) || !args[2].StartsWith("--ticket=",StringComparison.Ordinal))return Reject(output);
        var name=args[1][10..];var ticket=args[2][9..];
        if(name.Length is <1 or >60 || ticket.Length!=32)return Reject(output);
        foreach(var c in name)if(!char.IsAsciiLetterOrDigit(c)&&c!='-')return Reject(output);
        foreach(var c in ticket)if(!char.IsAsciiHexDigit(c))return Reject(output);
        return probe(name,ticket,output);
    }

    // Internal seam for the same-source unit checks, never an environment-variable override.
    internal static int RunInWorkspace(string[] args, string workspace, TextWriter output)
    {
        if (args.Length != 2 || args[0] != "--p15a-preflight" ||
            !args[1].StartsWith("--profile=", StringComparison.Ordinal))
            return Reject(output);
        var name = args[1][10..];
        if (name.Length is < 1 or > 60) return Reject(output);
        foreach (var c in name)
            if (!char.IsAsciiLetterOrDigit(c) && c != '-') return Reject(output);
        try
        {
            var root = Path.GetFullPath(workspace);
            RejectLinks(root);
            var proof = Path.Combine(root, "docs", "UPSTREAM_PROVENANCE.json");
            RejectLinks(proof);
            if (!File.Exists(proof)) return Reject(output);
            var profile = Path.Combine(root, ".p1-work", "p15a", "profiles", name);
            RejectLinks(profile);
            var marker = Path.Combine(profile, "PREFLIGHT_ONLY");
            var audit = Path.Combine(profile, "preflight.txt");
            RejectLinks(marker); RejectLinks(audit);
            if (Directory.Exists(profile) &&
                (!File.Exists(marker) || File.ReadAllText(marker) != Marker))
                return Reject(output);
            Directory.CreateDirectory(profile);
            if (!File.Exists(marker)) File.WriteAllText(marker, Marker);
            using var stream = new FileStream(audit, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using var writer = new StreamWriter(stream) { AutoFlush = true };
            stream.Seek(0, SeekOrigin.End);
            ProbeMockAsync().GetAwaiter().GetResult();
            writer.WriteLine("P15A_PREFLIGHT_ONLY_PASS; mock_probes=3; GUI_AUTHORIZED=false");
            output.WriteLine("P15A_PREFLIGHT_ONLY_PASS; GUI_AUTHORIZED=false; NORMAL_APP_RUNTIME=BLOCKED");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            // Do not print paths, session values or arbitrary exception payloads.
            output.WriteLine("P15A_PREFLIGHT_FAILED; GUI_AUTHORIZED=false; " + ex.GetType().Name);
            return Failed;
        }
    }

    private static Func<bool>? _guiPermit;
    internal static void DemandGuiAuthorization()
    {
        if(Volatile.Read(ref _guiPermit)?.Invoke()!=true)
            throw new InvalidOperationException("GUI startup refused: no live admitted offline-window capability.");
    }
    internal static IDisposable GrantOfflineGui(Func<bool> livePermit)
    {
        if(!livePermit() || Interlocked.CompareExchange(ref _guiPermit,livePermit,null)!=null)
            throw new InvalidOperationException("Offline GUI admission failed");
        return new GuiGrant();
    }
    private sealed class GuiGrant:IDisposable
    {
        public void Dispose()=>Interlocked.Exchange(ref _guiPermit,null);
    }
    internal static int RunGui(string[] args, TextWriter output, Func<string,string,bool,TextWriter,int> run)
    {
        if(args.Length!=4||args[0]!="--p15-offline-window"||(args[3]!="--selftest"&&args[3]!="--manual"))return Reject(output);
        return RunInfrastructure(new[]{"--p15a-r2-infrastructure",args[1],args[2]},output,(name,ticket,writer)=>run(name,ticket,args[3]=="--selftest",writer));
    }

    private static int Reject(TextWriter output)
    {
        output.WriteLine("P15A_STARTUP_REFUSED; GUI_AUTHORIZED=false; NORMAL_APP_RUNTIME=BLOCKED");
        return Refused;
    }

    private static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Reparse points are not admitted.");
    }

    private static async Task ProbeMockAsync()
    {
        using var handler = new TerminalMockHandler();
        using var client = new HttpClient(handler);
        foreach (var url in new[] { "https://example.invalid/p15a", "http://127.0.0.1:1/p15a", "https://localhost:1/p15a" })
        {
            using var response = await client.GetAsync(url).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                throw new InvalidOperationException("Terminal mock proof failed.");
        }
        if (handler.Requests != 3) throw new InvalidOperationException("Unexpected mock count.");
    }

    // Terminal HttpMessageHandler, with no InnerHandler, cookies, sockets or DNS.
    private sealed class TerminalMockHandler : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
