using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KugouAvaloniaPlayer.Services;

// Host-owned ephemeral admission table. A URL, cache row or metadata object can never grant admission.
// This is a trusted in-process module boundary, not a sandbox for arbitrary DLLs.
public static class ProviderStreamRegistry {
 public sealed record Input(Stream Stream,long Length,IDisposable Owner);
 sealed record Registration(Func<bool> Valid,Func<CancellationToken,Task<Input>> Open,string Lyrics,bool AllowPreparation);
 static readonly object gate=new();static readonly Dictionary<string,Registration> registrations=new(StringComparer.Ordinal);
 public static string Register(Func<bool> valid,Func<CancellationToken,Task<Input>> open,string lyrics,bool allowPreparation=true,bool desktopCatalog=false){
  // A list replacement can admit a new list while the old one still owns its handles.
  var capacity=desktopCatalog?2*PlaybackQueueManager.MaxQueueSize+2:32;
  lock(gate){if(registrations.Count>=capacity)throw new InvalidOperationException("Provider media capacity reached");if(lyrics.Length>131072)throw new InvalidDataException("Lyrics exceed limit");string id="provider-stream://"+Guid.NewGuid().ToString("N");registrations.Add(id,new(valid,open,lyrics,allowPreparation));return id;}
 }
 public static bool IsProvider(string? source)=>source?.StartsWith("provider-stream://",StringComparison.Ordinal)==true;
 static Registration Demand(string source){lock(gate){if(!registrations.TryGetValue(source,out var r)||!r.Valid())throw new UnauthorizedAccessException("Provider media grant revoked");return r;}}
 public static bool Allows(string? source){if(source==null||!IsProvider(source))return false;try{Demand(source);return true;}catch(UnauthorizedAccessException){return false;}}
 internal static bool AllowsPreparation(string source){try{return Demand(source).AllowPreparation;}catch(UnauthorizedAccessException){return false;}}
 public static string Lyrics(string source)=>Demand(source).Lyrics;
 public static async Task<Input> OpenAsync(string source,CancellationToken ct){var r=Demand(source);ct.ThrowIfCancellationRequested();var input=await r.Open(ct);try{ct.ThrowIfCancellationRequested();Demand(source);if(input.Length<=0||input.Length>128*1024*1024)throw new InvalidDataException("Invalid media length");return input;}catch{input.Stream.Dispose();input.Owner.Dispose();throw;}}
 public static void Remove(string source){lock(gate)registrations.Remove(source);}
 public static int Count {get {lock(gate)return registrations.Count;}}
}
