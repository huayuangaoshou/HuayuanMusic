using System.Text.Json;
using KugouAvaloniaPlayer.Services;
namespace HuayuanPreview;
// Uses the existing atomic SQLite settings store. No account or media secrets.
internal sealed record KugouRiskRecord(string Dlc,string Version,string Scope,DateTimeOffset ConfirmedAt,bool SaveLogin);
internal static class KugouRiskSettings {
 internal const string Dlc="kugou-concept",Version="2026-11-05.1",Scope="network,account-session,playlist-read,search,user-play,https-audio";
 const string Store="dlc_privacy",Key="kugou-concept";
 internal static KugouRiskRecord? Read(){try{var s=AppSqliteStore.LoadValue(Store,Key);return s==null?null:JsonSerializer.Deserialize<KugouRiskRecord>(s);}catch(JsonException){return null;}}
 internal static bool Matches(KugouRiskRecord? r,string version=Version,string scope=Scope)=>r!=null&&r.Dlc==Dlc&&r.Version==version&&r.Scope==scope&&r.ConfirmedAt> DateTimeOffset.UnixEpoch&&r.ConfirmedAt<=DateTimeOffset.UtcNow.AddMinutes(5);
 internal static void Confirm(bool saveLogin)=>AppSqliteStore.SaveValue(Store,Key,JsonSerializer.Serialize(new KugouRiskRecord(Dlc,Version,Scope,DateTimeOffset.UtcNow,saveLogin)));
 internal static void SavePreference(bool save){var r=Read();if(Matches(r))AppSqliteStore.SaveValue(Store,Key,JsonSerializer.Serialize(r! with{SaveLogin=save}));}
 internal static void Revoke()=>AppSqliteStore.DeleteValue(Store,Key);
}
