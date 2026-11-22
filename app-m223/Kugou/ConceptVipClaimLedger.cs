using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HuayuanMusic.Kugou.Gateway;

namespace HuayuanMusic.Provider.Kugou;

public sealed record ConceptVipClaimPreview(ConceptVipDailyStatus Status,
    ConceptVipEligibility Eligibility,ClaimAcknowledgement? PreviousOutcome);

/// <summary>
/// Durable, fail-closed account/day claim preflight. An UNKNOWN reservation is
/// committed to disk BEFORE any future claim dispatcher is allowed to run.
/// This class never performs network activity, grants eligibility or executes a claim.
/// </summary>
public sealed class ConceptVipClaimLedger
{
    public sealed record Entry(
        int Schema,string AccountKey,string Day,string SourceHash,
        string Nonce,ClaimAcknowledgement Outcome);

    public sealed record Reservation(
        Entry Entry,bool NewlyCreated,bool MayDispatch);

    static readonly JsonSerializerOptions JsonOptions=new()
    {
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
        MaxDepth=8
    };
    readonly string root;

    public ConceptVipClaimLedger(string isolatedRoot)
    {
        if(string.IsNullOrWhiteSpace(isolatedRoot))
            throw new ArgumentException("Explicit isolated profile storage root required");
        root=Path.GetFullPath(isolatedRoot);
        RejectReparse(root);
    }

    // Use only the account partition already computed from the SAME
    // KgSessionManager. No raw UserId, token, signature or media URLs on disk.
    static void ValidateAccountKey(string accountKey)
    {
        if(accountKey.Length!=64||accountKey.Any(c=>!char.IsAsciiHexDigit(c)||char.IsAsciiLetterLower(c)))
            throw new UnauthorizedAccessException("Invalid account partition");
    }
    static void RejectReparse(string path)
    {
        for(var dir=new DirectoryInfo(Path.GetFullPath(path));dir!=null;dir=dir.Parent)
        {
            if(dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint)!=0)
                throw new UnauthorizedAccessException("Linked or redirected ledger directory refused");
        }
        if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
            throw new UnauthorizedAccessException("Linked ledger file refused");
    }
    string FileFor(string accountKey,DateOnly day)
    {
        ValidateAccountKey(accountKey);
        var file=Path.Combine(root,accountKey,day.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)+".json");
        RejectReparse(file);
        return file;
    }
    static string Digest(string sourceId)=>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("HuayuanMusic:vip-source:"+sourceId)));

    public Entry? Read(string accountKey,DateOnly day)
    {
        var file=FileFor(accountKey,day);
        if(!File.Exists(file))return null;

        // A competing process may have atomically created the reservation but
        // still hold FileShare.None while flushing UNKNOWN. Only transient
        // sharing violations/temporarily empty fresh slots are retried. A
        // persistent partial/corrupt entry remains a hard fail-closed error.
        for(int attempt=0;attempt<25;attempt++)
        {
            try
            {
                var bytes=new FileInfo(file).Length;
                if(bytes==0 && attempt<24){Thread.Sleep(12);continue;}
                if(bytes is <=0 or >4096)
                    throw new InvalidDataException("Invalid claim ledger size");
                var entry=JsonSerializer.Deserialize<Entry>(File.ReadAllText(file),JsonOptions)
                    ??throw new InvalidDataException("Invalid claim ledger entry");
                if(entry.Schema!=1||entry.AccountKey!=accountKey||
                   entry.Day!=day.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)||
                   entry.SourceHash.Length!=64||entry.SourceHash.Any(c=>!char.IsAsciiHexDigit(c))||
                   entry.Nonce.Length!=32||entry.Nonce.Any(c=>!char.IsAsciiHexDigit(c))||
                   !Enum.IsDefined(entry.Outcome))
                    throw new InvalidDataException("Claim ledger identity or outcome mismatch");
                return entry;
            }
            catch(IOException e)when(attempt<24 && (e.HResult&0xFFFF) is 32 or 33)
            {
                Thread.Sleep(12);
            }
        }
        throw new IOException("Claim ledger is still locked; refuse rather than retry any claim");
    }

    // "separatePermissionVerified" is NOT an authorization system; only the
    // trusted host may assert it after checking separate platform/operation
    // approval. The production gateway does not currently call Begin.
    public Reservation Begin(string accountKey,ConceptVipDailyStatus status,
        ActivityApproval? approved,bool explicitUserClick,
        bool separatePermissionVerified)
    {
        var file=FileFor(accountKey,status.ServerDay);
        if(status.ReceivedToday)
            return new Reservation(new Entry(1,accountKey,
                status.ServerDay.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture),
                new string('0',64),new string('0',32),ClaimAcknowledgement.AlreadyReceived),false,false);

        var old=Read(accountKey,status.ServerDay);
        if(old!=null)return new Reservation(old,false,false);

        if(!explicitUserClick||!separatePermissionVerified||
           ConceptVipHistory.Assess(status,approved)!=
               ConceptVipEligibility.ReadyForSeparateClaimAuthorization)
            throw new UnauthorizedAccessException("No current independent claim permission");

        var source=Digest(approved!.SourceId);
        var entry=new Entry(1,accountKey,
            status.ServerDay.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture),
            source,Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            ClaimAcknowledgement.Unknown);
        Directory.CreateDirectory(root);
        var accountDir=Path.GetDirectoryName(file)!;
        RejectReparse(accountDir);
        Directory.CreateDirectory(accountDir);
        RejectReparse(file);
        try
        {
            // FileMode.CreateNew is atomic across processes; failure to persist
            // UNKNOWN is a hard refusal, not an opportunity to dispatch.
            using var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            JsonSerializer.Serialize(stream,entry,JsonOptions);
            stream.Flush(true);
            return new Reservation(entry,true,true);
        }
        catch(IOException)when(File.Exists(file))
        {
            return new Reservation(Read(accountKey,status.ServerDay)!,false,false);
        }
    }

    // Result storage is best-effort AFTER dispatch; failure leaves UNKNOWN
    // reservation, blocking retries. Never automatically reset the slot.
    public Entry Finish(Reservation reserved,ClaimAcknowledgement outcome)
    {
        if(!reserved.NewlyCreated||!reserved.MayDispatch||
           outcome is not (ClaimAcknowledgement.Confirmed or
                          ClaimAcknowledgement.Rejected or ClaimAcknowledgement.Unknown))
            throw new UnauthorizedAccessException("Unowned or invalid claim completion");
        var original=reserved.Entry;
        if(!DateOnly.TryParseExact(original.Day,"yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,out var day))
            throw new InvalidDataException("Invalid ledger day");
        var file=FileFor(original.AccountKey,day);
        var old=Read(original.AccountKey,day);
        if(old==null||old.Nonce!=original.Nonce||old.Outcome!=ClaimAcknowledgement.Unknown)
            throw new InvalidDataException("Claim reservation already completed or altered");
        var finished=old with{Outcome=outcome};
        var temp=file+"."+Convert.ToHexString(RandomNumberGenerator.GetBytes(8))+".tmp";
        try
        {
            using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                JsonSerializer.Serialize(output,finished,JsonOptions);
                output.Flush(true);
            }
            File.Replace(temp,file,null);
            return finished;
        }
        finally
        {
            // Only a newly created temporary file owned by this invocation.
            if(File.Exists(temp))File.Delete(temp);
        }
    }
}
