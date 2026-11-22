using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KuGou.Net.Protocol.Session;
using SimpleAudio;
namespace KugouAvaloniaPlayer.Services.Startup;

/// <summary>Process-lifetime profile, admitted before production data services. Never GUI permission.</summary>
internal sealed class OfflineStartupProfile : IDisposable
{
    private static readonly object Gate = new();
    private static volatile OfflineStartupProfile? _current;
    private static bool _ordinaryPathsObserved;
    internal static bool Active => _current != null;
    internal static OfflineStartupProfile Current => _current ?? throw new InvalidOperationException("No admitted offline profile");
    private readonly FileStream _lease;
    private readonly Dictionary<string,string> _media;
    private Dictionary<string,string> _imports=new(StringComparer.OrdinalIgnoreCase);
    internal bool Import { get; private set; }
    internal void RegisterImports(Dictionary<string,string> entries)=>_imports=new(entries,StringComparer.OrdinalIgnoreCase);
    internal string Root { get; }
    internal bool Fault { get; }
    internal bool Gui { get; }
    private volatile bool _disposed;
    internal bool LiveGuiPermit => Gui && !_disposed && ReferenceEquals(_current,this);
    internal string Roaming => Path.Combine(Root,"roaming");
    internal string Local => Path.Combine(Root,"local");
    internal string Temp => Path.Combine(Root,"temp");
    internal string Media => Path.Combine(Root,"media");
    internal ISessionPersistence Session { get; } = new InMemorySessionPersistence();
    private OfflineStartupProfile(string root, Dictionary<string,string> media, FileStream lease,bool fault,bool gui) { Gui=gui;Fault=fault;Root=root;_media=new(media);_lease=lease; }
    internal static string DataRoot(Environment.SpecialFolder folder)
    {
        lock(Gate)
        {
            if(_current is { } p) { p.ValidateTree(p.Root);return folder==Environment.SpecialFolder.ApplicationData?p.Roaming:p.Local; }
            _ordinaryPathsObserved=true;
            var root=Environment.GetFolderPath(folder);
            if(string.IsNullOrWhiteSpace(root))throw new InvalidOperationException("Cannot resolve Huayuan data root");
            return Path.Combine(root,"HuayuanMusic-M223");
        }
    }
    internal static OfflineStartupProfile Install(string name,string ticket)
    {
        lock(Gate)
        {
            if(_current!=null||_ordinaryPathsObserved)throw new InvalidOperationException("Profile is immutable or data roots already observed");
            // Root follows the app marker, never the original user profile or the formal repository.
            var directory=new DirectoryInfo(AppContext.BaseDirectory);
            while(directory!=null&&!File.Exists(Path.Combine(directory.FullName,"PREVIEW_ONLY")))directory=directory.Parent;
            if(directory is null)throw new IOException("Isolated runtime root marker missing");
            var workspace=directory.FullName;
            RejectLinks(workspace);
            var work=Path.Combine(workspace,"profiles-auth");
            var permitPath=Path.Combine(work,"authorizations",name+".json");
            RejectLinks(permitPath);
            var permit=JsonSerializer.Deserialize(File.ReadAllText(permitPath),ProfilePermitContext.Default.ProfilePermit) ?? throw new InvalidDataException("Missing profile permit");
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ticket)));
            if(permit.Name!=name||!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash),Encoding.ASCII.GetBytes(permit.TicketHash)))throw new InvalidDataException("Profile permit mismatch");
            var root=Path.Combine(work,"profiles",name);RejectLinks(root);
            var marker=Path.Combine(root,"OFFLINE_PROFILE_V1");RejectLinks(marker);
            if(File.ReadAllText(marker)!=name+"\n"+permit.TicketHash)throw new InvalidDataException("Profile marker mismatch");
            ValidateExistingTree(root);
            foreach(var item in permit.Media)
            {
                var p=Path.GetFullPath(Path.Combine(root,"media",item.Key));
                if(!Under(p,Path.Combine(root,"media")))throw new InvalidDataException("Media escape");
                if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))!=item.Value)throw new InvalidDataException("Media integrity failure");
            }
            var lease=new FileStream(Path.Combine(root,"PROFILE.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            try
            {
                AudioCacheLocation.ConfigureIsolatedLocalRoot(Path.Combine(root,"local"));
                var profile=new OfflineStartupProfile(root,permit.Media,lease,permit.Fault,permit.Gui);
                profile.Import=permit.Import;
                profile.RegisterImports(OfflineMediaImport.ReadLedger(profile));
                IsolatedSessionPersistence.Install(profile.Session);
                _current=profile;
                Directory.CreateDirectory(_current.Roaming);Directory.CreateDirectory(_current.Local);Directory.CreateDirectory(_current.Temp);
                Environment.SetEnvironmentVariable("TEMP",_current.Temp,EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("TMP",_current.Temp,EnvironmentVariableTarget.Process);
                return _current;
            }
            catch { lease.Dispose();throw; }
        }
    }
    internal static bool Under(string path,string root)=>path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    internal static void RejectLinks(string path)
    {
        for(string? p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))
            try { if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked path rejected"); } catch(FileNotFoundException) { } catch(DirectoryNotFoundException) { }
    }
    // One synchronous validation pass. Every existing component is checked, but
    // shared ancestors are not probed thousands of times in the same pass.
    internal static void RejectLinks(string path, HashSet<string> checkedPaths)
    {
        for(string? p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))
        {
            if(checkedPaths.Contains(p))break;
            try
            {
                if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked path rejected");
                checkedPaths.Add(p);
            }
            catch(FileNotFoundException) { }
            catch(DirectoryNotFoundException) { }
        }
    }
    private static void ValidateExistingTree(string root)
    {
        var checkedPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(root);
        void Walk(string directory)
        {
            RejectLinks(directory,checkedPaths);
            foreach(var p in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectLinks(p,checkedPaths);if(Directory.Exists(p))Walk(p);
            }
        }
    }
    internal void ValidateTree(string path) { if(path!=Root&&!Under(path,Root))throw new IOException("Profile escape");ValidateExistingTree(path); }
    internal bool AllowsMedia(string? source) => source!=null && OfflineMediaImport.Supported(source) && AllowsFile(source);
    private bool AllowsFile(string? source)
    {
        try
        {
            if(source==null||!Path.IsPathFullyQualified(source)||source.StartsWith(@"\\",StringComparison.Ordinal))return false;
            var p=Path.GetFullPath(source);if(!Under(p,Media))return false;RejectLinks(p);
            return (_media.TryGetValue(Path.GetRelativePath(Media,p).Replace('\\','/'),out var hash)||_imports.TryGetValue(Path.GetRelativePath(Media,p).Replace('\\','/'),out hash))&&File.Exists(p)&&OfflineMediaImport.Hash(p)==hash;
        }
        catch(Exception e) when(e is IOException or ArgumentException or UnauthorizedAccessException){return false;}
    }
    internal void ValidateMediaTree(string path)
    {
        if(path!=Media&&!Under(Path.GetFullPath(path),Media))throw new IOException("Outside admitted media");
        ValidateExistingTree(path);
        foreach(var file in Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories))if(!AllowsFile(file))throw new IOException("Unadmitted media content");
    }
    internal void DemandMedia(string path) { if(!AllowsMedia(path))throw new IOException("Only admitted synthetic media is available"); }
    // Disposal releases ownership; deliberately never permits a second profile in this process.
    public void Dispose() { _disposed=true;_lease.Dispose(); }
}
internal sealed record ProfilePermit(string Name,string TicketHash,Dictionary<string,string> Media,bool Fault=false,bool Gui=false,bool Import=false);
[JsonSerializable(typeof(ProfilePermit))]
internal partial class ProfilePermitContext:JsonSerializerContext { }
