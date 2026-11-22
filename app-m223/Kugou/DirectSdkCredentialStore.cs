namespace HuayuanMusic.Provider.Kugou;

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

// Reuses the Windows Credential Manager API already used by HuayuanMusic's approved provider.
// This isolated candidate owns ONLY its own target, never reads KA Music or another application.
internal sealed record SavedProbeSession(int Version,string UserId,string Token,DateTimeOffset SavedAtUtc);

internal static class DirectSdkCredentialStore
{
    private const string Target="HuayuanMusic/Kugou/IntegratedAccount/v1";
    private static readonly string TestTarget="HuayuanMusic/Kugou/IntegratedAccount/v1/SELFTEST/"+Guid.NewGuid().ToString("N");
    private const int CredentialNotFound=1168;
    private const uint Generic=1, LocalMachine=2;
    private const int MaxBytes=2560;

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }
    [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool ReadNative(string target,uint type,uint flags,out IntPtr ptr);
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool WriteNative(ref Credential data,uint flags);
    [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool DeleteNative(string target,uint type,uint flags);
    [DllImport("advapi32.dll",EntryPoint="CredFree")]
    private static extern void FreeNative(IntPtr ptr);

    internal static void Save(string userId,string token)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows credential storage required");
        if(string.IsNullOrWhiteSpace(userId)||!userId.All(char.IsAsciiDigit)||
           string.IsNullOrWhiteSpace(token)||token.Length>1600)
            throw new InvalidOperationException("登录态字段不符合安全保存条件");
        Write(Target,new SavedProbeSession(1,userId,token,DateTimeOffset.UtcNow));
    }
    private static void Write(string target,SavedProbeSession data)
    {
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(data);
        if(bytes.Length is 0 or >MaxBytes)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new InvalidOperationException("登录态超出 Windows 凭据容量");
        }
        IntPtr ptr=Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes,0,ptr,bytes.Length);
            var cred=new Credential{Type=Generic,TargetName=target,Blob=ptr,
                BlobSize=(uint)bytes.Length,Persist=LocalMachine,UserName="HuayuanMusic isolated session"};
            if(!WriteNative(ref cred,0))throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows Credential Manager write failed");
        }
        finally
        {
            for(int i=0;i<bytes.Length;i++)Marshal.WriteByte(ptr,i,0);
            Marshal.FreeHGlobal(ptr);
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    internal static bool TryLoad(out SavedProbeSession? result)
    {
        result=Read(Target);
        if(result==null)return false;
        if(result.Version!=1||string.IsNullOrWhiteSpace(result.UserId)||
           !result.UserId.All(char.IsAsciiDigit)||string.IsNullOrWhiteSpace(result.Token)||
           result.Token.Length>1600||result.SavedAtUtc>DateTimeOffset.UtcNow.AddMinutes(3)||
           result.SavedAtUtc<DateTimeOffset.UtcNow.AddDays(-30))
        {
            result=null;
            throw new InvalidDataException("保存的登录态版本或期限不满足要求，需要重新扫码");
        }
        return true;
    }
    private static SavedProbeSession? Read(string target)
    {
        if(!ReadNative(target,Generic,0,out IntPtr credPtr))
        {
            int code=Marshal.GetLastWin32Error();
            if(code==CredentialNotFound)return null;
            throw new Win32Exception(code,"Windows Credential Manager read failed");
        }
        try
        {
            var credential=Marshal.PtrToStructure<Credential>(credPtr);
            if(credential.Type!=Generic||credential.BlobSize is 0 or >MaxBytes||
               credential.Blob==IntPtr.Zero)throw new InvalidDataException("Invalid credential blob");
            var bytes=new byte[credential.BlobSize];
            try
            {
                Marshal.Copy(credential.Blob,bytes,0,bytes.Length);
                return JsonSerializer.Deserialize<SavedProbeSession>(bytes);
            }
            finally{CryptographicOperations.ZeroMemory(bytes);}
        }
        finally{FreeNative(credPtr);}
    }
    // Explicit one-time import from HuayuanMusic's OWN previously validated R2 experiment.
    // NEVER called automatically; never reads any KA Music or third-party client's storage.
    internal static bool ImportFromVerifiedProbeOnce()
    {
        const string ownProbe="HuayuanMusic/Kugou/BassProbeAccount/v1";
        if(Read(Target)!=null)throw new InvalidOperationException("集成版已有保存登录；不会覆盖，请先手动清除");
        var previous=Read(ownProbe);
        if(previous==null)return false;
        if(previous.Version!=1||string.IsNullOrWhiteSpace(previous.UserId)||
           !previous.UserId.All(char.IsAsciiDigit)||string.IsNullOrWhiteSpace(previous.Token)||
           previous.Token.Length>1600||previous.SavedAtUtc>DateTimeOffset.UtcNow.AddMinutes(3)||
           previous.SavedAtUtc<DateTimeOffset.UtcNow.AddDays(-30))
            throw new InvalidDataException("实验登录已过期或不符合导入条件");
        Write(Target,previous);
        return true;
    }
    internal static void Delete()
    {
        Remove(Target);
    }
    private static void Remove(string target)
    {
        if(!DeleteNative(target,Generic,0))
        {
            int code=Marshal.GetLastWin32Error();
            if(code!=CredentialNotFound)throw new Win32Exception(code,"Windows Credential Manager delete failed");
        }
    }
    internal static async Task<bool> DeletionSelfTest()
    {
        var fake=new SavedProbeSession(1,"10000001","dummy-token-only",DateTimeOffset.UtcNow);
        int calls=0;
        try {
            Write(TestTarget,fake);
            var cancel=await CredentialDeletion.RunAsync(()=>Task.FromResult(false),()=>{calls++;Remove(TestTarget);});
            if(cancel!=CredentialDeletionResult.Cancelled||calls!=0||Read(TestTarget)?.Token!=fake.Token)return false;
            var failed=await CredentialDeletion.RunAsync(()=>Task.FromResult(true),()=>throw new IOException("synthetic deletion failure"));
            if(failed!=CredentialDeletionResult.Failed||Read(TestTarget)==null)return false;
            var deleted=await CredentialDeletion.RunAsync(()=>Task.FromResult(true),()=>{calls++;Remove(TestTarget);});
            return deleted==CredentialDeletionResult.Deleted&&calls==1&&Read(TestTarget)==null;
        } finally { Remove(TestTarget); }
    }
    internal static bool SelfTest()
    {
        // Uses a dummy, separately scoped credential. No user's real login data is read.
        var fake=new SavedProbeSession(1,"10000001","dummy-token-only",DateTimeOffset.UtcNow);
        try
        {
            Write(TestTarget,fake);
            var read=Read(TestTarget);
            return read is {Version:1,UserId:"10000001",Token:"dummy-token-only"};
        }
        finally{Remove(TestTarget);}
    }
}