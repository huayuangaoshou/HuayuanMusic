namespace HuayuanMusic.Provider.Contracts;
// Cover metadata never carries account auth or audio signatures. Exact image CDN origins only.
public static class ArtworkUrl {
 public static event Action<string>? Diagnostic;
 static string? Reject(string reason){Diagnostic?.Invoke(reason);return null;}
 public static string? Normalize(string? value){
  if(string.IsNullOrWhiteSpace(value))return Reject("NO_FIELD");
  value=value.Trim().Replace("%7Bsize%7D","400",StringComparison.OrdinalIgnoreCase).Replace("{size}","400",StringComparison.OrdinalIgnoreCase);
  if(value.StartsWith("//",StringComparison.Ordinal))value="https:"+value;
  // Legacy metadata often advertises HTTP even though the same verified CDN supports TLS.
  // Upgrade the metadata before validation; HTTP is never sent and is never a fallback.
  if(value.StartsWith("http://",StringComparison.OrdinalIgnoreCase))value="https://"+value[7..];
  if(new[]{"token","secret","signature","cookie","credential","password","key="}.Any(marker=>value.Contains(marker,StringComparison.OrdinalIgnoreCase))||value.Contains('%')||value.Length>2048||!Uri.TryCreate(value,UriKind.Absolute,out var u)||u.Scheme!="https"||u.Port!=443||u.HostNameType!=UriHostNameType.Dns||u.IsLoopback||u.UserInfo.Length!=0||u.Query.Length!=0||u.Fragment.Length!=0||value.Any(char.IsControl)||u.AbsolutePath.Contains('%')||u.AbsolutePath.Contains('@'))return Reject("URL_REJECTED");
  if(u.IdnHost is not("imge.kugou.com" or "imgessl.kugou.com" or "p1fx.kgimg.com" or "p2fx.kgimg.com" or "p3fx.kgimg.com" or "p4fx.kgimg.com"))return Reject("URL_REJECTED");
  if(!new[]{".jpg",".jpeg",".png",".webp"}.Any(e=>u.AbsolutePath.EndsWith(e,StringComparison.OrdinalIgnoreCase)))return Reject("URL_REJECTED");
  return u.AbsoluteUri;
 }
 public static void Validate(string? value){if(value!=null&&Normalize(value)!=value)throw new ArgumentException("Unsafe cover metadata");}
}
