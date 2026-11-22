using System.Security.Cryptography;
using System.Text.Json;
using KugouAvaloniaPlayer.Services.Startup;
namespace HuayuanPreview;
internal static class LocalFirstStartup {
 internal static string[] Resolve(string[] args){
  if(args.Length is not (1 or 2)||args[0]!="--local-first")return args;
  bool selftest=args.Length==2&&args[1]=="--selftest";if(args.Length==2&&!selftest)throw new IOException("Unsupported startup option");
  var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory!=null&&!File.Exists(Path.Combine(directory.FullName,"PREVIEW_ONLY")))directory=directory.Parent;
  if(directory is null)throw new IOException("No isolated candidate root");
  string packet=Path.Combine(directory.FullName,"control","local-startup.json");OfflineStartupProfile.RejectLinks(packet);
  if(new FileInfo(packet).Length>16384)throw new IOException("Startup packet too large");
  using var doc=JsonDocument.Parse(File.ReadAllText(packet));var v=doc.RootElement;
  foreach(var n in new[]{"HuayuanMusic.P17.Nightflight.dll","HuayuanMusic.P1.dll"}){
   string file=Path.Combine(AppContext.BaseDirectory,n);OfflineStartupProfile.RejectLinks(file);
   using var f=File.OpenRead(file);if(Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant()!=v.GetProperty("binaries").GetProperty(n).GetString())throw new IOException("Local startup binary changed");
  }
  string name=v.GetProperty("name").GetString()??"",ticket=v.GetProperty("ticket").GetString()??"";
  if(!name.StartsWith("p17-body-r3-manual-",StringComparison.Ordinal))throw new IOException("Not an authorized local profile");
  if(selftest&&!name.StartsWith("p17-body-r3-manual-test-",StringComparison.Ordinal))throw new IOException("Selftest cannot run against a human-use profile");
  // Goes through the same complete immutable profile/media/HTTP/lease gate, not an App bypass.
  return ["--p15-offline-window","--profile="+name,"--ticket="+ticket,selftest?"--selftest":"--manual"];
 }
}
