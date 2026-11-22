namespace HuayuanMusic.Provider.Kugou;
public enum CredentialDeletionResult { Cancelled, Deleted, Failed }
public static class CredentialDeletion {
 public static async Task<CredentialDeletionResult> RunAsync(Func<Task<bool>> confirm,Action delete){
  if(!await confirm())return CredentialDeletionResult.Cancelled;
  try{delete();return CredentialDeletionResult.Deleted;}catch{return CredentialDeletionResult.Failed;}
 }
}
