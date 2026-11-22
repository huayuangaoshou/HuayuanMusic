using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HuayuanMusic.Kugou.Gateway;
using HuayuanMusic.Provider.Kugou;
using KugouAvaloniaPlayer.Services.Startup;

namespace HuayuanPreview;

// A reviewed operator provisions this application's CURRENT USER credential, never another client's session.
// Storage access proves user/profile scope, not platform permission. Do not log this object or its JSON.
internal sealed class ApprovedKugouConfiguration {
 public int Schema{get;set;}
 public string ProfileRoot{get;set;}="";
 public ApplicationIdentity Application{get;set;}=null!;
 public DeviceAdmission Device{get;set;}=null!;
 public ProtocolContext Protocol{get;set;}=new();
 [System.Text.Json.Serialization.JsonIgnore]
 public AuthorizedIdentity Identity=>new(){AppId=Application.AppId,QrIssuerAppId=Application.QrIssuerAppId,SourceAppId=Application.SourceAppId,ClientVersion=Application.ClientVersion,PlatformPermissionReference=Application.PlatformPermissionReference,Dfid=Device.Dfid,Mid=Device.Mid,Uuid=Device.Uuid,ApprovedProductId=Protocol.ProductId,ApprovedPageId=Protocol.PageId,ApprovedProtocolParameters=Protocol.Parameters};
 public GatewayPermit Permit{get;set;}=null!;
 public SigningMaterial Signing{get;set;}=null!;
 public ManualClaimApproval? ManualClaim{get;set;}
 internal sealed class ManualClaimApproval {
  public string HumanApprovalReference{get;set;}="";
  public DateTimeOffset NotAfter{get;set;}
  public ActivityApproval Activity{get;set;}=null!;
  public override string ToString()=>"ManualClaimApproval [redacted]";
 }
 public override string ToString()=>"ApprovedKugouConfiguration [redacted]";
 internal sealed class ApplicationIdentity {
  public string AppId{get;set;}="";public string QrIssuerAppId{get;set;}="";public string SourceAppId{get;set;}="";public string ClientVersion{get;set;}="";public string PlatformPermissionReference{get;set;}="";
  public override string ToString()=>"ApplicationIdentity [redacted]";
 }
 internal sealed class DeviceAdmission {
  public string Dfid{get;set;}="";public string Mid{get;set;}="";public string Uuid{get;set;}="";
  public override string ToString()=>"DeviceAdmission [redacted]";
 }
 internal sealed class ProtocolContext {
  public string? ProductId{get;set;}public string? PageId{get;set;}public Dictionary<string,string> Parameters{get;set;}=new();
  public override string ToString()=>"ProtocolContext [redacted]";
 }
 internal sealed class SigningMaterial {
  public string Android{get;set;}="";public string Web{get;set;}="";public string Media{get;set;}="";
  public override string ToString()=>"SigningMaterial [redacted]";
 }
}

internal sealed class ApprovedKugouConnectionProvider {
 readonly string profile;
 internal ApprovedKugouConnectionProvider(string profile)=>this.profile=Path.GetFullPath(profile).TrimEnd(Path.DirectorySeparatorChar);
 internal string Target=>"HuayuanMusic/Kugou/ApprovedConnection/v1/"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.ToUpperInvariant())));
 internal IntegratedSdkGateway Connect()=>Connect(null);
 // Terminal construction is only available inside the exact gated self-test process. Normal startup cannot inject a factory.
 internal IntegratedSdkGateway Connect(Func<ApprovedKugouConfiguration,IntegratedSdkGateway>? terminal){
  if(terminal!=null&&!OfflineGuiApplication.SelfTest)throw new UnauthorizedAccessException("Terminal injection requires the existing self-test gate");
  byte[] bytes=CurrentUserKugouCredential.Read(Target);
  try{
   ApprovedKugouConfiguration c;
   try{c=JsonSerializer.Deserialize<ApprovedKugouConfiguration>(bytes,JsonOptions())??throw new JsonException();}
   catch(JsonException){throw new UnauthorizedAccessException("获准配置格式无效；未发送请求");}
   if(c.Schema!=1||!string.Equals(c.ProfileRoot,profile,StringComparison.OrdinalIgnoreCase)||c.Application==null||c.Device==null||c.Protocol?.Parameters==null||c.Permit==null||c.Signing==null)
    throw new UnauthorizedAccessException("获准配置版本或 profile 不匹配；未发送请求");
   var p=c.Permit;
   if(p.Operations==null||p.MediaHosts==null||p.QrImageHosts==null||string.IsNullOrWhiteSpace(c.Identity.PlatformPermissionReference)||string.IsNullOrWhiteSpace(p.HumanApprovalReference)||p.NotAfter<=DateTimeOffset.UtcNow||p.NotAfter>DateTimeOffset.UtcNow.AddHours(24)||p.Operations.Count==0)
    throw new UnauthorizedAccessException("缺少当前平台/操作批准或批准已过期；未发送请求");
   // This connection path is mature SDK only. Claim remains separately approved by the existing activity service.
   if(p.Operations.Any(x=>!Enum.IsDefined(x)||x is GatewayOperation.UserVerify or GatewayOperation.SongAuthorize or GatewayOperation.ResolveAudio or GatewayOperation.FreeListen))
    throw new UnauthorizedAccessException("本配置只允许成熟 SDK 连接");
   ActivityApproval? activity=null;
   if(p.Operations.Contains(GatewayOperation.ClaimDaily)){
    var claim=c.ManualClaim;var rules=claim?.Activity;
    if(claim==null||string.IsNullOrWhiteSpace(claim.HumanApprovalReference)||claim.HumanApprovalReference==p.HumanApprovalReference||claim.NotAfter<p.NotAfter||rules==null||string.IsNullOrWhiteSpace(rules.RulesReference)||!rules.EligibilityConfirmed||rules.RequiresOfficialClientOrAd||string.IsNullOrEmpty(rules.SourceId)||!rules.SourceId.All(char.IsAsciiDigit)||rules.Day==default||!p.Operations.Contains(GatewayOperation.ClaimHistory)||!p.Operations.Contains(GatewayOperation.Entitlement))
     throw new UnauthorizedAccessException("领取需独立且覆盖连接有效期的人工批准、适用活动规则、资格及记录/权益查询范围");
    activity=rules; // The existing service checks the actual SERVER day, history and outcome ledger at claim time.
   }
   IntegratedSdkGateway Attach(IntegratedSdkGateway sdk){sdk.ApprovedActivity=activity;return sdk;}
   if(new[]{c.Signing.Android,c.Signing.Web,c.Signing.Media}.Any(string.IsNullOrWhiteSpace))throw new UnauthorizedAccessException("签名材料缺失；未发送请求");
   if(terminal!=null)return Attach(terminal(c));
   if(!p.LiveNetworkApproved)throw new UnauthorizedAccessException("配置未批准真实传输；未发送请求");
   // Validation/construction only; these constructors perform no account, QR or audio request.
   var signer=new CredentialBoundSigner(c.Signing.Android,c.Signing.Web,c.Signing.Media);
   OwnedSigningWire? wire=null;ConceptGateway? concept=null;AuthorizedAudioTransport? audio=null;
   try{
    wire=new OwnedSigningWire(AuthorizedHttpWire.CreateLive(c.Identity,p,signer),signer);
    concept=new ConceptGateway("kugou-approved",c.Identity,wire);
    audio=p.AllowAudioTransport?AuthorizedAudioTransport.CreateLive(p):new AuthorizedAudioTransport(new HttpClient(new NoAudioTransport()),p);
    return Attach(new IntegratedSdkGateway(concept,audio)); // MatureSdk remains the default.
   }catch{audio?.Dispose();if(concept!=null)concept.Dispose();else wire?.Dispose();signer.Dispose();throw;}
  }finally{CryptographicOperations.ZeroMemory(bytes);}
 }
 internal static JsonSerializerOptions JsonOptions(){var o=new JsonSerializerOptions{MaxDepth=12,UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow};o.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter<GatewayOperation>());o.Converters.Add(new SetConverter<string>());o.Converters.Add(new SetConverter<GatewayOperation>());return o;}
 sealed class SetConverter<T>:System.Text.Json.Serialization.JsonConverter<IReadOnlySet<T>> {
  public override IReadOnlySet<T> Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>JsonSerializer.Deserialize<HashSet<T>>(ref reader,options)??throw new JsonException();
  public override void Write(Utf8JsonWriter writer,IReadOnlySet<T> value,JsonSerializerOptions options)=>JsonSerializer.Serialize(writer,value.ToArray(),options);
 }
 sealed class NoAudioTransport:HttpMessageHandler {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>throw new UnauthorizedAccessException("音频传输尚未单独批准");
 }
 // Existing ConceptGateway owns its wire; extend that ownership to the supplied signer as well.
 sealed class OwnedSigningWire(IConceptWire inner,CredentialBoundSigner signer):IConceptWire {
  public Task<byte[]> ReadQrImageAsync(Uri uri,CancellationToken ct)=>inner.ReadQrImageAsync(uri,ct);
  public Task<JsonElement> SendAsync(GatewayOperation op,KuGou.Net.Protocol.Transport.KgRequest request,string uid,string token,CancellationToken ct)=>inner.SendAsync(op,request,uid,token,ct);
  public void Dispose(){try{inner.Dispose();}finally{signer.Dispose();}}
 }
}

internal static class CurrentUserKugouCredential {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]internal struct Credential {
  public uint Flags,Type;public string TargetName;public string? Comment;public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
  public uint BlobSize;public IntPtr Blob;public uint Persist,AttributeCount;public IntPtr Attributes;public string? TargetAlias,UserName;
 }
 [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool ReadNative(string target,uint type,uint flags,out IntPtr pointer);
 [DllImport("advapi32.dll",EntryPoint="CredFree")]static extern void FreeNative(IntPtr pointer);
 [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool WriteNative(ref Credential credential,uint flags);
 [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool DeleteNative(string target,uint type,uint flags);
 internal static void WriteTest(string target,byte[] bytes){
  if(!OfflineGuiApplication.SelfTest||!target.StartsWith("HuayuanMusic/Kugou/ApprovedConnection/v1/",StringComparison.Ordinal))throw new UnauthorizedAccessException();
  if(bytes.Length is 0 or >2560)throw new InvalidOperationException("Test credential size");
  var ptr=Marshal.AllocHGlobal(bytes.Length);try{Marshal.Copy(bytes,0,ptr,bytes.Length);var c=new Credential{Type=1,TargetName=target,BlobSize=(uint)bytes.Length,Blob=ptr,Persist=1,UserName="HuayuanMusic offline selftest"};if(!WriteNative(ref c,0))throw new InvalidOperationException("Test credential write failed");}finally{for(int i=0;i<bytes.Length;i++)Marshal.WriteByte(ptr,i,0);Marshal.FreeHGlobal(ptr);}
 }
 internal static void DeleteTest(string target){if(!OfflineGuiApplication.SelfTest)throw new UnauthorizedAccessException();if(!DeleteNative(target,1,0))throw new InvalidOperationException("Test credential cleanup failed");}
 internal static byte[] Read(string target){
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows credential storage required");
  if(!ReadNative(target,1,0,out var ptr))throw new UnauthorizedAccessException("当前 profile 没有可读的获准凭据；未发送请求");
  try{var c=Marshal.PtrToStructure<Credential>(ptr);if(c.Type!=1||c.BlobSize is 0 or >2560||c.Blob==IntPtr.Zero)throw new UnauthorizedAccessException("获准凭据大小或类型无效");var bytes=new byte[c.BlobSize];Marshal.Copy(c.Blob,bytes,0,bytes.Length);return bytes;}
  finally{var c=Marshal.PtrToStructure<Credential>(ptr);if(c.Blob!=IntPtr.Zero&&c.BlobSize<=2560)for(int i=0;i<c.BlobSize;i++)Marshal.WriteByte(c.Blob,i,0);FreeNative(ptr);}
 }
}
