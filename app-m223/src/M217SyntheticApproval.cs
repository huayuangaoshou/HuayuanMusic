using System.Security.Cryptography;
using System.Text;
using KugouAvaloniaPlayer.Services.Startup;

namespace HuayuanPreview;

// Test-only protocol. No SDK or live startup calls this class.
internal static class M217SyntheticApproval
{
    internal sealed record Scope(string Purpose,string Candidate,string SourceSha,string RuntimeSha,
        string Account,string Session,string Playlist,int PlaylistOrdinal,int MemberOrdinal,string Track,
        int Append,int Play,int Seconds,int VipGets,int SourceGets,int MediaGets,bool ProxyOnly);
    internal sealed record Permit(Guid Id,Scope Scope,DateTimeOffset Expires);
    // Derive independent fixture IDs from the fresh random profile suffix so all
    // three test processes reproduce the same IDs without saving a permit.
    static Guid FixtureId(string profile, string phase)
    {
        byte[] digest=SHA256.HashData(Encoding.UTF8.GetBytes(profile+":"+phase));
        return new Guid(digest.AsSpan(0,16));
    }

    static FileStream Reserve(string directory,Guid id)=>new(
        Path.Combine(directory,id.ToString("N")+".spent"),FileMode.CreateNew,FileAccess.Write,
        FileShare.None,1,FileOptions.WriteThrough);

    static bool TryConsume(string directory, Permit permit, Scope expected, DateTimeOffset now)
    {
        if(expected.Purpose!="M217_OFFLINE_SYNTHETIC"||
           !expected.Account.StartsWith("synthetic-",StringComparison.Ordinal)||
           permit.Id==Guid.Empty||permit.Scope!=expected||permit.Expires<=now||
           expected.PlaylistOrdinal!=2||expected.MemberOrdinal!=1||expected.Append!=2||
           expected.Play!=1||expected.Seconds!=6||expected.VipGets!=1||expected.SourceGets!=1||
           expected.MediaGets!=1||!expected.ProxyOnly)
            return false;

        OfflineStartupProfile.RejectLinks(directory);
        try
        {
            // CreateNew is the consume operation. Even a zero-length file burns the ID.
            using var reservation=Reserve(directory,permit.Id);
            reservation.Flush(flushToDisk:true);
            return true;
        }
        catch(IOException){return false;}
        catch(UnauthorizedAccessException){return false;}
    }

    internal static async Task<int> RunAsync(string phase)
    {
        var profile=OfflineStartupProfile.Current.Root;
        string profileName=Path.GetFileName(profile);
        if(!profileName.StartsWith("p17-body-r3-manual-test-r2-f2-",StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Synthetic approval fixture profile required");
        var directory=Path.Combine(profile,"m217-synthetic-spent");
        OfflineStartupProfile.RejectLinks(directory);
        Directory.CreateDirectory(directory);
        // Scope identity is independent of the runtime/profile admission ticket.
        var scope=new Scope("M217_OFFLINE_SYNTHETIC","synthetic-candidate",new string('a',64),new string('b',64),
            "synthetic-account","synthetic-session","synthetic-list",2,1,"synthetic-track",2,1,6,1,1,1,true);
        var now=DateTimeOffset.UtcNow;
        var permit=new Permit(FixtureId(profileName,"consumed"),scope,now.AddMinutes(1));
        var crashId=FixtureId(profileName,"crash");
        if(phase=="crash")
        {
            // Simulate termination after atomic creation and before any success acknowledgment.
            using var reservation=Reserve(directory,crashId);
            Environment.Exit(73);
        }
        if(phase=="restart")
        {
            if(TryConsume(directory,permit,scope,now)||TryConsume(directory,permit with{Id=crashId},scope,now))
                throw new InvalidOperationException("Consumed permit accepted after restart");
            return 2;
        }
        if(phase!="first")throw new ArgumentException("Unknown offline phase");
        foreach(var wrong in new[]{scope with{Account="synthetic-other"},scope with{Session="other"},
            scope with{Playlist="other"},scope with{MemberOrdinal=2},scope with{Track="other"},
            scope with{RuntimeSha=new string('c',64)},scope with{SourceSha=new string('d',64)},
            scope with{Seconds=7},scope with{ProxyOnly=false},scope with{MediaGets=2}})
            if(TryConsume(directory,permit with{Scope=wrong},scope,now))throw new InvalidOperationException("Wrong scope consumed");
        if(TryConsume(directory,permit with{Expires=now},scope,now))throw new InvalidOperationException("Expired permit consumed");
        // A second UI dispatch and concurrent requests compete for the same file.
        var results=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>Task.Run(()=>TryConsume(directory,permit,scope,now))));
        if(results.Count(x=>x)!=1||TryConsume(directory,permit,scope,now))
            throw new InvalidOperationException("Synthetic permit budget reused");
        return 13;
    }
}
