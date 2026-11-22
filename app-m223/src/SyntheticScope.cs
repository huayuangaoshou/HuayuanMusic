using System.Security.Cryptography;
using System.Text.Json;
using KugouAvaloniaPlayer.Services.Startup;
namespace HuayuanPreview;
internal static class SyntheticScope {
 internal static bool NamedForThisHost(string name)=>name.StartsWith("p17-body-r3-",StringComparison.Ordinal)&&name.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-');
 internal static void Validate(){
  var clock=System.Diagnostics.Stopwatch.StartNew();Console.WriteLine("P19_ADMISSION_BEGIN");
  var profile=OfflineStartupProfile.Current;if(profile.Import&&!(Path.GetFileName(profile.Root).StartsWith("p17-body-r3-manual-",StringComparison.Ordinal)||Path.GetFileName(profile.Root).StartsWith("p17-body-r3-suite-",StringComparison.Ordinal)))throw new IOException("This host does not authorize imports");profile.ValidateMediaTree(profile.Media);
  var assembly=typeof(SyntheticScope).Assembly;using var json=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(x=>x.EndsWith("expected-media.json",StringComparison.Ordinal)))!;
  var expected=JsonSerializer.Deserialize<Dictionary<string,string>>(json)??throw new IOException("Missing pinned synthetic manifest");
  if(OfflineGuiApplication.SelfTest&&Path.GetFileName(profile.Root).StartsWith("p17-body-r3-suitequeue-",StringComparison.Ordinal)){using var extra=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(x=>x.EndsWith("queue-media.json",StringComparison.Ordinal)))!;foreach(var pair in JsonSerializer.Deserialize<Dictionary<string,string>>(extra)!)expected.Add(pair.Key,pair.Value);}
  // Startup is serialized before App/services: validate one complete ledger snapshot, not N full reparses.
  var admittedImports=profile.Import?OfflineMediaImport.ReadLedger(profile):new Dictionary<string,string>();
  var actual=Directory.GetFiles(profile.Media,"*",SearchOption.AllDirectories);if(!profile.Import&&actual.Length!=expected.Count)throw new IOException("Unexpected media tree");
  foreach(var file in actual){var key=Path.GetRelativePath(profile.Media,file).Replace('\\','/');if(!expected.TryGetValue(key,out var hash)){if(profile.Import&&key.StartsWith("imported/",StringComparison.Ordinal)&&admittedImports.ContainsKey(key)){profile.DemandMedia(file);continue;}throw new IOException("Unexpected media file");}using var stream=File.OpenRead(file);if(Convert.ToHexString(SHA256.HashData(stream))!=hash.ToUpperInvariant())throw new IOException("Only pinned mathematical audio is admitted");}
  Console.WriteLine($"P19_ADMISSION_END ms={clock.Elapsed.TotalMilliseconds:F3}; files={actual.Length}");
 }
}
