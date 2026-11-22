using KuGou.Net.Clients;
using KuGou.Net.Protocol.Raw;
using Microsoft.Extensions.Logging.Abstractions;
namespace HuayuanMusic.Kugou.Gateway;
public sealed partial class ConceptGateway {
 LoginClient SdkLogin(CancellationToken ct)=>new(new RawLoginApi(Sdk(ct),session,NullLogger<RawLoginApi>.Instance),session,NullLogger<LoginClient>.Instance);
 async Task<byte[]?> ReadQrImage(string source,CancellationToken ct){
  if(string.IsNullOrEmpty(source))return null;
  byte[] bytes;
  if(source.StartsWith("data:image/png;base64,",StringComparison.Ordinal)){
   if(source.Length>1400000)throw new GatewayFailure("QR_IMAGE_TOO_LARGE");bytes=Convert.FromBase64String(source[22..]);
  }else{
   if(!Uri.TryCreate(source,UriKind.Absolute,out var address))throw new GatewayFailure("QR_IMAGE_ADDRESS_INVALID");
   bytes=await wire.ReadQrImageAsync(address,ct);
  }
  if(bytes.Length<24||bytes.Length>1048576||!bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new GatewayFailure("QR_PNG_REQUIRED");
  var width=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16,4));var height=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20,4));
  if(width is 0 or >1024||height is 0 or >1024)throw new GatewayFailure("QR_IMAGE_DIMENSIONS_INVALID");return bytes;
 }
}
