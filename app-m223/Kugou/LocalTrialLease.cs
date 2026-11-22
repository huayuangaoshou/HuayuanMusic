using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace HuayuanMusic.Provider.Kugou;

public sealed class LocalTrialLease
{
    public const string Scope="M222:index=1,member=1,vip=1,source=1,media=1,list=2,memberOrdinal=1,append=2,play=1,ms=6000";
    const string RealRoot=@"D:\AI\Projects\HuayuanMusic\.p1-work\m2-22-controlled-trial-20261118";
    const string ExeName="HuayuanMusic.P17.Nightflight.exe";
    readonly CancellationTokenSource deadline=new(TimeSpan.FromMinutes(4));
    readonly long admitted=System.Diagnostics.Stopwatch.GetTimestamp();
    public double ElapsedMs=>System.Diagnostics.Stopwatch.GetElapsedTime(admitted).TotalMilliseconds;
    int attached;
    public bool Real { get; }
    public CancellationToken Token=>deadline.Token;
    private LocalTrialLease(bool real)=>Real=real;
    public void Stop()=>deadline.Cancel();
    internal void Demand()=>Token.ThrowIfCancellationRequested();
    internal void Attach(bool real)
    {
        Demand();
        if(Real!=real||Interlocked.Exchange(ref attached,1)!=0)
            throw new UnauthorizedAccessException("Trial lease factory already used or wrong mode");
    }

    public static LocalTrialLease? ConfirmReal()=>Confirm(true,Guid.Empty);
    public static LocalTrialLease? ConfirmSynthetic(Guid run)=>run!=Guid.Empty?Confirm(false,run):null;

    static LocalTrialLease? Confirm(bool real,Guid run)
    {
        string exe=Path.GetFullPath(Environment.ProcessPath!);
        string runtime=Path.GetDirectoryName(exe)!;
        string root=Path.GetDirectoryName(runtime)!;
        if(!OperatingSystem.IsWindows()||!Environment.UserInteractive||
           !string.Equals(exe,Path.Combine(root,"preview",ExeName),StringComparison.OrdinalIgnoreCase)||
           (real&&!root.Equals(RealRoot,StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("Unexpected trial installation path");
        string ledger=real?root:Path.Combine(root,"evidence","m222-test-locks",run.ToString("N"));
        string marker=Path.Combine(ledger,real?"M222_REAL_ATTEMPT_CONSUMED_ONCE":"M222_SYNTHETIC_SPENT");
        var checkedPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CheckPath(marker,checkedPaths);
        if(File.Exists(marker)||Directory.Exists(marker))return null;
        var seal=CheckSeal(root,runtime,checkedPaths);
        string request=real?Path.Combine(root,"M222_REAL_REVIEW_REQUEST.json"):Path.Combine(ledger,"M222_SYNTHETIC_REQUEST.json");
        var intent=ReadRequest(request,seal,real);
        if(intent==null||!Ask(real))return null;
        checkedPaths.Clear();
        if(seal!=CheckSeal(root,runtime,checkedPaths)||intent!=ReadRequest(request,seal,real))
            throw new IOException("Trial installation or review request changed");
        CheckPath(marker,checkedPaths);
        if(!real)Directory.CreateDirectory(ledger);
        CheckPath(marker,new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        using var record=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough);
        JsonSerializer.Serialize(record,new{scope=Scope,mode=real?"real":"synthetic",createdUtc=DateTimeOffset.UtcNow,
            exe=seal.Exe,source=seal.Source,runtime=seal.Runtime,nonceDigest=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(intent.Nonce)))});
        record.Flush(true);
        return new LocalTrialLease(real);
    }

    // The review request permits showing the dialog. Only the user's in-process
    // confirmation and CreateNew below can grant the one-shot lease.
    sealed record ReviewRequest(string Mode,string Scope,string Exe,string Source,string Runtime,
        string Nonce,DateTimeOffset IssuedUtc,DateTimeOffset ExpiresUtc);

    static ReviewRequest? ReadRequest(string path,Seal seal,bool real)
    {
        CheckPath(path,new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if(!File.Exists(path))return null;
        using var input=File.OpenRead(path);
        if(input.Length is <=0 or >4096)return null;
        var request=JsonSerializer.Deserialize<ReviewRequest>(input);
        var now=DateTimeOffset.UtcNow;
        if(request==null||request.Mode!=(real?"real":"synthetic")||request.Scope!=Scope||
           request.Exe!=seal.Exe||request.Source!=seal.Source||request.Runtime!=seal.Runtime||
           !Guid.TryParseExact(request.Nonce,"N",out var nonce)||nonce==Guid.Empty||
           request.IssuedUtc>now||request.ExpiresUtc<=now||
           request.ExpiresUtc-request.IssuedUtc>TimeSpan.FromMinutes(5))
            return null;
        return request;
    }

    static void CheckPath(string path,HashSet<string> visited)
    {
        for(string? p=Path.GetFullPath(path);p!=null&&visited.Add(p);p=Path.GetDirectoryName(p))
            try
            {
                if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Trial linked path");
            }
            catch(FileNotFoundException) { }
            catch(DirectoryNotFoundException) { }
    }

    sealed record Seal(string Scope,string Exe,string Source,string Runtime);
    static Seal CheckSeal(string root,string runtime,HashSet<string> visited)
    {
        string PathOf(string relative)
        {
            if(Path.IsPathRooted(relative)||relative.Contains('\\')||relative.Split('/').Any(x=>x is "." or ".." or ""))
                throw new IOException("Invalid manifest path");
            string p=Path.GetFullPath(Path.Combine(root,relative));
            if(!p.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new IOException("Manifest path escaped root");
            CheckPath(p,visited);
            return p;
        }
        string Sha(string relative)
        {
            using var input=File.OpenRead(PathOf(relative));
            return Convert.ToHexStringLower(SHA256.HashData(input));
        }
        var seal=JsonSerializer.Deserialize<Seal>(File.ReadAllText(PathOf("f2/M222_SEAL.json")));
        if(seal==null||seal.Scope!=Scope||seal.Source!=Sha("evidence/build-ready.json")||
           seal.Runtime!=Sha("f2/runtime-sha256.json")||seal.Exe!=Sha("preview/"+ExeName))
            throw new IOException("Trial seal mismatch");
        var entries=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(PathOf("f2/runtime-sha256.json")))!;
        var actual=new HashSet<string>(StringComparer.Ordinal);
        void CheckTree(string folder)
        {
            CheckPath(folder,visited);
            foreach(var child in Directory.EnumerateFileSystemEntries(folder))
            {
                CheckPath(child,visited);
                if(Directory.Exists(child))CheckTree(child);
                else actual.Add(Path.GetRelativePath(root,child).Replace('\\','/'));
            }
        }
        CheckTree(runtime);
        if(!actual.SetEquals(entries.Keys)||actual.Count(n=>Path.GetFileName(n).Equals("bass.dll",StringComparison.OrdinalIgnoreCase))!=1||
           entries.Any(p=>Sha(p.Key)!=p.Value))
            throw new IOException("Trial runtime inventory mismatch");
        using var source=JsonDocument.Parse(File.ReadAllText(PathOf("evidence/build-ready.json")));
        foreach(var entry in source.RootElement.GetProperty("inputs").EnumerateObject())
            if(Sha(entry.Name)!=entry.Value.GetString())throw new IOException("Trial source inventory mismatch");
        return seal;
    }

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode,Pack=1)]
    struct Dialog
    {
        public uint Size;
        public IntPtr Parent,Instance;
        public uint Flags,Common;
        public string Title;
        public IntPtr Icon;
        public string Heading,Content;
        public uint ButtonCount;
        public IntPtr Buttons;
        public int DefaultButton;
        public uint RadioCount;
        public IntPtr RadioButtons;
        public int DefaultRadio;
        public IntPtr Verification,Expanded,ExpandedControl,CollapsedControl,FooterIcon,Footer,Callback,CallbackData;
        public uint Width;
    }

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode,Pack=1)]
    struct Button
    {
        public int Id;
        public string Text;
    }

    [DllImport("comctl32.dll",ExactSpelling=true,CharSet=CharSet.Unicode)]
    static extern int TaskDialogIndirect(ref Dialog config,out int button,IntPtr radio,IntPtr checkedState);

    static bool Ask(bool real)
    {
        var approve=new Button{Id=100,Text=real?"批准本次单次试听":"批准合成离线试听"};
        IntPtr buttons=Marshal.AllocHGlobal(Marshal.SizeOf<Button>());
        Marshal.StructureToPtr(approve,buttons,false);
        try
        {
            var dialog=new Dialog{Size=(uint)Marshal.SizeOf<Dialog>(),Flags=8,Common=8,
                Title=real?"M222 单次真实试听":"M222 Synthetic Offline Consent",
                Heading=real?"原始 HTTP 媒体为明文传输":"纯合成测试 · 不读取真实账号",
                Content="本次最多各一次：歌单索引、目标成员、VIP、音源、媒体。仅第二歌单第一首，手动入队两次，只播放第二个队列条目一次。原生停止最多 6 秒；失败不重试。退出时回退临时队列、选中项和音量，撤销会话并释放播放资源。媒体只经现有非旁路系统代理。取消为默认选项。",
                ButtonCount=1,Buttons=buttons,DefaultButton=2};
            return TaskDialogIndirect(ref dialog,out int result,IntPtr.Zero,IntPtr.Zero)==0&&result==100;
        }
        finally{Marshal.DestroyStructure<Button>(buttons);Marshal.FreeHGlobal(buttons);}
    }
}

