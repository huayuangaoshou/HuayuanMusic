using System.Diagnostics.Tracing;
namespace HuayuanPreview;
// Optional sampled allocation evidence. Not an exact object census and not enabled in normal playback.
internal sealed class R3AllocationObserver:EventListener {
 readonly object gate=new();readonly Dictionary<string,(long Ticks,long Bytes)> types=new();long events;
 protected override void OnEventSourceCreated(EventSource source){if(source.Name=="Microsoft-Windows-DotNETRuntime")EnableEvents(source,EventLevel.Verbose,(EventKeywords)1);}
 protected override void OnEventWritten(EventWrittenEventArgs e){
  if(e.EventName?.StartsWith("GCAllocationTick",StringComparison.Ordinal)!=true||e.PayloadNames==null||e.Payload==null||gate==null)return;
  int name=e.PayloadNames.IndexOf("TypeName"),bytes=e.PayloadNames.IndexOf("AllocationAmount64");if(name<0)return;
  string type=e.Payload[name]?.ToString()??"unknown";long size=bytes>=0?Convert.ToInt64(e.Payload[bytes]):0;
  lock(gate){events++;if(types.Count<256||types.ContainsKey(type)){types.TryGetValue(type,out var prior);types[type]=(prior.Ticks+1,prior.Bytes+size);}}
 }
 internal object Snapshot(){lock(gate)return new {events,estimatedOnly=true,types=types.OrderByDescending(x=>x.Value.Bytes).Take(25).Select(x=>new {type=x.Key,ticks=x.Value.Ticks,sampledMiB=x.Value.Bytes/1048576d}).ToArray()};}
}
