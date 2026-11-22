using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KugouAvaloniaPlayer.Services.Startup;

namespace HuayuanPreview;

/// <summary>
/// Windows EXE owned, offline-only startup. Reuses existing NormalStartupGate,
/// OfflineStartupProfile and production player: never starts a second playback engine.
/// A renamed .NET apphost is a UI entry point, not an authorization bypass.
/// </summary>
internal static class NativeDailyStartup
{
    private sealed record Session(string Name,string Ticket);
    private sealed record Permit(string Name,string TicketHash,Dictionary<string,string> Media,
        bool Fault=false,bool Gui=true,bool Import=true);

    internal static string[] Resolve(string[] args)
    {
        var exe=Path.GetFileName(Environment.ProcessPath);
        if(!string.Equals(exe,"HuayuanMusic.M1.Local.exe",StringComparison.OrdinalIgnoreCase))
            return args;
        // No arguments for everyday double click. Other args are deliberately refused.
        if(args.Length!=0)throw new IOException("Native M1 host refuses command-line overrides");
        var root=FindRoot();
        OfflineStartupProfile.RejectLinks(root);
        var auth=Path.Combine(root,"profiles-auth");
        OfflineStartupProfile.RejectLinks(auth);
        Directory.CreateDirectory(auth);
        var statePath=Path.Combine(auth,"m1-native-session.json");
        OfflineStartupProfile.RejectLinks(statePath);
        Session state;
        if(File.Exists(statePath))
        {
            if(new FileInfo(statePath).Length is <=0 or >8192)
                throw new IOException("Invalid M1 daily state length");
            state=JsonSerializer.Deserialize<Session>(File.ReadAllText(statePath))
                ??throw new InvalidDataException("Invalid M1 daily state");
            ValidateState(auth,state);
        }
        else
        {
            // Existing profiles are never adopted. A crashed partial provisioning must be
            // examined, not silently deleted or overwritten.
            var name="p17-body-r3-manual-native-"+Guid.NewGuid().ToString("N")[..10];
            var ticket=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            state=new Session(name,ticket);
            Provision(root,auth,state);
            WriteExclusive(statePath,JsonSerializer.Serialize(state));
            ValidateState(auth,state);
        }
        Environment.SetEnvironmentVariable("M1_LOCAL_DAILY","1",EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("CORE02_DEMO",null,EnvironmentVariableTarget.Process);
        return ["--p15-offline-window","--profile="+state.Name,
                "--ticket="+state.Ticket,"--manual"];
    }

    internal static string[] ProvisionNewTrial()
    {
        var root=FindRoot();
        var name="p17-body-r3-manual-test-r2-f2-trial-"+Guid.NewGuid().ToString("N")[..10];
        var state=new Session(name,Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        Provision(root,Path.Combine(root,"profiles-auth"),state);
        return ["--p15-offline-window","--profile="+state.Name,"--ticket="+state.Ticket,"--manual"];
    }

    private static string FindRoot()
    {
        for(DirectoryInfo? p=new(AppContext.BaseDirectory);p!=null;p=p.Parent)
        {
            var marker=Path.Combine(p.FullName,"PREVIEW_ONLY");
            OfflineStartupProfile.RejectLinks(marker);
            if(File.Exists(marker))return p.FullName;
        }
        throw new IOException("M1 isolated root marker not found");
    }
    private static void ValidateState(string auth,Session state)
    {
        if(!state.Name.StartsWith("p17-body-r3-manual-native-",StringComparison.Ordinal)
           ||state.Name.Length>60
           ||!state.Name.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-')
           ||state.Ticket.Length!=32||!state.Ticket.All(Uri.IsHexDigit))
            throw new InvalidDataException("M1 state not admissible");
        var profile=Path.Combine(auth,"profiles",state.Name);
        var permitFile=Path.Combine(auth,"authorizations",state.Name+".json");
        var markerFile=Path.Combine(profile,"OFFLINE_PROFILE_V1");
        foreach(var path in new[]{profile,permitFile,markerFile})
            OfflineStartupProfile.RejectLinks(path);
        using var doc=JsonDocument.Parse(File.ReadAllText(permitFile));
        var permit=doc.RootElement;
        var stored=permit.GetProperty("TicketHash").GetString()??"";
        var hashed=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state.Ticket)));
        if(permit.GetProperty("Name").GetString()!=state.Name||
           !permit.GetProperty("Gui").GetBoolean()||
           !permit.GetProperty("Import").GetBoolean()||
           stored.Length!=64||
           !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(stored),
               Encoding.ASCII.GetBytes(hashed))||
           File.ReadAllText(markerFile)!=state.Name+"\n"+hashed)
            throw new InvalidDataException("Existing M1 state or permit changed");
    }
    private static void WriteExclusive(string path,string value)
    {
        OfflineStartupProfile.RejectLinks(path);
        using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        using var writer=new StreamWriter(file,new UTF8Encoding(false));
        writer.Write(value);
        writer.Flush();
        file.Flush(true);
    }
    private static void Provision(string root,string auth,Session state)
    {
        var profile=Path.Combine(auth,"profiles",state.Name);
        var authorizations=Path.Combine(auth,"authorizations");
        OfflineStartupProfile.RejectLinks(profile);
        OfflineStartupProfile.RejectLinks(authorizations);
        Directory.CreateDirectory(profile);
        Directory.CreateDirectory(authorizations);
        var media=Path.Combine(profile,"media");
        Directory.CreateDirectory(media);
        var resource=typeof(NativeDailyStartup).Assembly;
        var resourceName=resource.GetManifestResourceNames().Single(x=>x.EndsWith(".expected-media.json",StringComparison.Ordinal));
        using var input=resource.GetManifestResourceStream(resourceName)??throw new IOException("Missing built-in media manifest");
        var manifest=JsonSerializer.Deserialize<Dictionary<string,string>>(input)
           ??throw new IOException("Invalid built-in media manifest");
        var verified=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var item in manifest)
        {
            if(item.Key.Contains("..",StringComparison.Ordinal)||item.Key.StartsWith('/')
               ||item.Key.Contains('\\')||item.Key.Contains(':'))
                throw new InvalidDataException("Fixture path escape");
            var source=Path.GetFullPath(Path.Combine(root,"seed-media",item.Key));
            var target=Path.GetFullPath(Path.Combine(media,item.Key));
            if(!source.StartsWith(Path.Combine(root,"seed-media")+Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
               ||!target.StartsWith(media+Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Fixture path outside isolated media");
            OfflineStartupProfile.RejectLinks(source);
            OfflineStartupProfile.RejectLinks(target);
            var bytes=File.ReadAllBytes(source);
            var actual=Convert.ToHexString(SHA256.HashData(bytes));
            if(actual!=item.Value.ToUpperInvariant())throw new InvalidDataException("Fixture hash mismatch");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {output.Write(bytes);output.Flush(true);}
            verified.Add(item.Key,actual);
        }
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state.Ticket)));
        WriteExclusive(Path.Combine(profile,"OFFLINE_PROFILE_V1"),state.Name+"\n"+hash);
        foreach(var folder in new[]{"roaming","local","temp"})
            Directory.CreateDirectory(Path.Combine(profile,folder));
        var permit=new Permit(state.Name,hash,verified);
        WriteExclusive(Path.Combine(authorizations,state.Name+".json"),
            JsonSerializer.Serialize(permit));
        // The existing production Sqlite service creates its own schema on first start.
        // No personal database or AppData folder is opened here.
    }
}
