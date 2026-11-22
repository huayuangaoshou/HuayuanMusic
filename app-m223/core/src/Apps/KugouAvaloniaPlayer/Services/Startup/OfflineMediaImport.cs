using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace KugouAvaloniaPlayer.Services.Startup;

// Only invoked by an explicit file selection (or owned synthetic selftest input).
// This is an admission ledger, not another music database or tag parser.
internal static class OfflineMediaImport
{
    internal const long MaxBytes = 256L * 1024 * 1024;
    internal const int MaxFiles = 20;
    internal static bool Supported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mp3" or ".flac" or ".wav";
    internal static string Hash(string path) { using var f=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(f)); }
    private static string Ledger(OfflineStartupProfile p)=>Path.Combine(p.Root,"imports.v1.tsv");
    private static string Header(OfflineStartupProfile p)=>"HUAYUAN_IMPORT_V1\t"+Path.GetFileName(p.Root);
    internal static Dictionary<string,string> ReadLedger(OfflineStartupProfile p)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var path=Ledger(p);OfflineStartupProfile.RejectLinks(path);
        if(!File.Exists(path))return result;
        if(!p.Import || new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException("Import ledger not authorized or too large");
        var lines=File.ReadAllLines(path);if(lines.Length==0||lines[0]!=Header(p))throw new InvalidDataException("Cross-profile import ledger");
        var checkedPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var line in lines.Skip(1))
        {
            var fields=line.Split('\t');if(fields.Length!=2||fields[1].Length!=64||!fields[1].All(Uri.IsHexDigit))throw new InvalidDataException("Invalid import ledger");
            var full=Path.GetFullPath(Path.Combine(p.Media,fields[0]));
            if(!fields[0].StartsWith("imported/",StringComparison.Ordinal)||!OfflineStartupProfile.Under(full,Path.Combine(p.Media,"imported"))||!Supported(full)||Path.GetRelativePath(p.Media,full).Replace('\\','/')!=fields[0])throw new InvalidDataException("Import ledger escape");
            OfflineStartupProfile.RejectLinks(full,checkedPaths);
            result.Add(fields[0],fields[1]);
        }
        return result;
    }
    internal static void SaveLedger(OfflineStartupProfile p,Dictionary<string,string> entries)
    {
        if(entries.Count>5000)throw new IOException("本阶段最多导入 5000 个受控副本");
        var path=Ledger(p);OfflineStartupProfile.RejectLinks(path);
        var temp=Path.Combine(p.Temp,Guid.NewGuid()+".ledger-partial");
        try
        {
            using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { var bytes=Encoding.UTF8.GetBytes(Header(p)+"\n"+string.Join("\n",entries.Select(e=>e.Key+"\t"+e.Value))+"\n");f.Write(bytes);f.Flush(true); }
            File.Move(temp,path,true);
        }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
    internal sealed record Result(string Source,string Status,string? Copy=null);
    internal static async Task<Result> CopySelectedAsync(string selected,CancellationToken cancel,IProgress<long>? progress=null)
    {
        var p=OfflineStartupProfile.Current;
        if(!p.LiveGuiPermit||!p.Import)throw new IOException("当前许可不允许用户文件导入");
        if(!Path.IsPathFullyQualified(selected)||selected.StartsWith(@"\\",StringComparison.Ordinal)||selected.AsSpan(2).Contains(':'))throw new IOException("仅允许明确选择的本地常规文件");
        var source=Path.GetFullPath(selected);
        var drive=new DriveInfo(Path.GetPathRoot(source)!);
        if(drive.DriveType is not (DriveType.Fixed or DriveType.Removable))throw new IOException("拒绝网络或未知驱动器；仅导入本地常规文件");
        OfflineStartupProfile.RejectLinks(source);
        if(!Supported(source))throw new IOException("仅支持 MP3 / FLAC / WAV");
        var name=Path.GetFileName(source);if(name.Length>140||name.IndexOfAny(new[]{'\t','\n','\r'})>=0)throw new IOException("文件名过长或包含控制字符");
        using var sourceParents=LockDirectories(Path.GetDirectoryName(source)!);
        using var profileParents=LockDirectories(p.Root);
        using var tempParents=LockDirectories(p.Temp);
        // Deny replacement, deletion and modification of the opened source on Windows.
        await using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);
        CheckHandle(input.SafeFileHandle,source);OfflineStartupProfile.RejectLinks(source);
        if(input.Length is <=0 or >MaxBytes)throw new IOException("文件为空或超过 256 MiB 单文件上限");
        p.ValidateTree(p.Root);
        var temporary=Path.Combine(p.Temp,Guid.NewGuid()+Path.GetExtension(source));
        string? complete=null;bool committed=false,created=false;
        try
        {
            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
            {
                var buffer=new byte[65536];long copied=0;int count;
                while((count=await input.ReadAsync(buffer,cancel))>0)
                {
                    await output.WriteAsync(buffer.AsMemory(0,count),cancel);copied+=count;progress?.Report(copied);
                    cancel.ThrowIfCancellationRequested();
                }
                await output.FlushAsync(cancel);output.Flush(true);
            }
            cancel.ThrowIfCancellationRequested();CheckHandle(input.SafeFileHandle,source);OfflineStartupProfile.RejectLinks(source);
            input.Position=0;var digest=Convert.ToHexString(await SHA256.HashDataAsync(input,cancel));
            if(digest!=Hash(temporary)||new FileInfo(temporary).Length!=input.Length)throw new IOException("副本校验失败");
            ValidateMetadataBudget(temporary);
            var tag=new ATL.Track(temporary);
            var duration=tag.DurationMs>0?tag.DurationMs/1000d:tag.Duration;
            if(!double.IsFinite(duration)||duration<=0||duration>24*3600)throw new InvalidDataException("无法识别有效音频及时长");
            var entries=ReadLedger(p);
            var old=entries.FirstOrDefault(e=>e.Value==digest);
            if(old.Key!=null)
            {
                var existing=Path.Combine(p.Media,old.Key);p.DemandMedia(existing);
                return new Result(name,"重复（复用既有受控副本）",existing);
            }
            var relative="imported/"+digest+"/"+name;
            complete=Path.Combine(p.Media,relative);OfflineStartupProfile.RejectLinks(complete);
            Directory.CreateDirectory(Path.GetDirectoryName(complete)!);
            using var destinationParents=LockDirectories(Path.GetDirectoryName(complete)!);
            cancel.ThrowIfCancellationRequested();
            if(File.Exists(complete))throw new IOException("未登记目标已存在，请保留现场检查");
            File.Move(temporary,complete);created=true;
            entries.Add(relative,digest);SaveLedger(p,entries);committed=true;p.RegisterImports(entries);
            return new Result(name,"成功",complete);
        }
        finally
        {
            if(File.Exists(temporary))File.Delete(temporary);
            if(created&&!committed&&complete!=null&&File.Exists(complete))File.Delete(complete);
        }
    }
    // Bound metadata containers before ATL's lazy picture allocation. ATL remains the parser.
    internal static void ValidateMetadataBudget(string path)
    {
        using var f=File.OpenRead(path);Span<byte> h=stackalloc byte[10];if(f.Read(h)<10)throw new InvalidDataException("音频过短");
        const int max=4*1024*1024;
        if(h[0]=='I'&&h[1]=='D'&&h[2]=='3')
        {
            if((h[6]|h[7]|h[8]|h[9])>=128)throw new InvalidDataException("Invalid ID3 size");
            var size=(h[6]<<21)|(h[7]<<14)|(h[8]<<7)|h[9];if(size>max||size+10>f.Length)throw new InvalidDataException("ID3 标签过大或截断");
        }
        if(h[0]=='f'&&h[1]=='L'&&h[2]=='a'&&h[3]=='C')
        {
            f.Position=4;Span<byte> block=stackalloc byte[4];long total=0;bool last=false;
            while(!last)
            {
                f.ReadExactly(block);last=(block[0]&128)!=0;var size=(block[1]<<16)|(block[2]<<8)|block[3];total+=size+4;
                if(size>max||total>2*max||f.Position+size>f.Length)throw new InvalidDataException("FLAC 元数据过大或截断");
                f.Position+=size;
            }
        }
        if(h[0]=='R'&&h[1]=='I'&&h[2]=='F'&&h[3]=='F')
        {
            f.Position=12;Span<byte> block=stackalloc byte[8];int count=0;long metadata=0;
            while(f.Position+8<=f.Length)
            {
                if(++count>4096)throw new InvalidDataException("WAV chunk limit");
                f.ReadExactly(block);long size=BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
                if(f.Position+size>f.Length)throw new InvalidDataException("Truncated WAV chunk");
                if(!(block[0]=='d'&&block[1]=='a'&&block[2]=='t'&&block[3]=='a'))metadata+=size;
                if(metadata>max)throw new InvalidDataException("WAV metadata limit");
                f.Position+=size+(size&1);
            }
        }
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle,StringBuilder path,uint size,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    internal sealed class DirectoryLocks:IDisposable
    {
        internal readonly List<SafeFileHandle> Handles=new();
        public void Dispose(){foreach(var handle in Handles)handle.Dispose();}
    }
    internal static DirectoryLocks LockDirectories(string directory)
    {
        var locks=new DirectoryLocks();
        try
        {
            // Open root-to-leaf. Denying write/delete on the directory handles prevents
            // renaming ancestors or replacing them with junctions during admission.
            var parents=new Stack<string>();for(string? path=directory;path!=null;path=Path.GetDirectoryName(path))parents.Push(path);
            foreach(var path in parents)
            {
                var handle=CreateFile(path,0x80,1,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
                if(handle.IsInvalid){handle.Dispose();throw new IOException("无法锁定所选文件的目录边界");}
                locks.Handles.Add(handle);OfflineStartupProfile.RejectLinks(path);CheckHandle(handle,path);
            }
            return locks;
        }
        catch{locks.Dispose();throw;}
    }
    internal static void CheckHandle(SafeFileHandle handle,string expected)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows file admission required");
        var name=new StringBuilder(32768);var length=GetFinalPathNameByHandle(handle,name,32768,0);
        if(length==0||length>=32768||!string.Equals(name.ToString(),@"\\?\"+expected,StringComparison.OrdinalIgnoreCase))throw new IOException("源文件被替换或路径发生变化");
    }
}
