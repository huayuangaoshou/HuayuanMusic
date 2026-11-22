namespace HuayuanMusic.Provider.Kugou;

/// <summary>Fixed compiled module admission, not arbitrary DLL discovery.</summary>
public sealed record TrustedModuleDescriptor(string Id,string Version,int ContractRevision,bool FixtureOnly) {
 public static TrustedModuleDescriptor Current {get;}=new(KugouProvider.Identity,"0.1.0",1,true);
 public void Validate(int hostContractRevision){
  if(Id!=KugouProvider.Identity||Version!="0.1.0"||ContractRevision!=hostContractRevision||!FixtureOnly)
   throw new NotSupportedException("Untrusted or incompatible module descriptor");
 }
}
