namespace HuayuanPreview;
// Session UI permission is distinct from short-lived media grants; never persisted.
internal sealed class KugouSessionConsent {
 internal bool Approved{get;private set;}string? alias,key;long version;
 internal async Task<bool> RequestAsync(Func<Task<bool>> confirm){var epoch=version;if(Approved)return true;bool yes=await confirm();if(epoch!=version)return false;Approved=yes;return yes;}
 internal bool Reconcile(bool connected,string? currentAlias,string? currentKey){
  if(!Approved)return false;
  if(!connected||currentAlias==null||(alias!=null&&alias!=currentAlias)||(key!=null&&key!=currentKey)){Revoke();return false;}
  alias=currentAlias;key??=currentKey;return true;
 }
 internal void Revoke(){version++;Approved=false;alias=null;key=null;}
}
