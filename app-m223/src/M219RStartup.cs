using System.Collections;
using System.Security.Cryptography;
using HuayuanMusic.Provider.Kugou;
using KugouAvaloniaPlayer.Services;
using Microsoft.Data.Sqlite;

namespace HuayuanPreview;

internal static class M219RStartup
{
    internal static LocalTrialLease? Lease { get; private set; }
    internal static bool Real=>Lease?.Real==true;
    internal static bool Active=>Lease!=null;
    internal static bool GateOnly { get; private set; }
    internal static bool HoldSyntheticProbe { get; private set; }

    internal static string[]? Enter(string[] args)
    {
        var modes=args.Where(a=>a.StartsWith("--m222-",StringComparison.Ordinal)).ToArray();
        if(modes.Length==0)return args;
        if(modes.Length!=1)return null;
        if(modes[0]=="--m222-live")
        {
            if(args.Length!=1)return null;
            Lease=LocalTrialLease.ConfirmReal();
            if(Lease==null)return null;
            // No inherited diagnostics, synthetic switches or legacy routes in this mode.
            foreach(DictionaryEntry v in Environment.GetEnvironmentVariables())
            {
                string key=(string)v.Key;
                string n=key.ToUpperInvariant();
                if((n.Length>=2&&n[0] is 'M' or 'P'&&char.IsAsciiDigit(n[1]))||
                   n.StartsWith("KUGOU_")||n.StartsWith("BODY_")||n.StartsWith("CORE02_")||
                   n.StartsWith("R2_")||n.StartsWith("R3_"))
                    Environment.SetEnvironmentVariable(key,null);
            }
            Environment.SetEnvironmentVariable("M208B_R2_UI","1");
            return NativeDailyStartup.ProvisionNewTrial();
        }
        HoldSyntheticProbe=modes[0].StartsWith("--m222-probe-hold=",StringComparison.Ordinal);
        bool gateOnly=HoldSyntheticProbe||modes[0].StartsWith("--m222-probe=",StringComparison.Ordinal);
        string prefix=HoldSyntheticProbe?"--m222-probe-hold=":gateOnly?"--m222-probe=":"--m222-test=";
        if(!modes[0].StartsWith(prefix,StringComparison.Ordinal)||
           !Guid.TryParseExact(modes[0][prefix.Length..],"N",out var run))
            return null;
        var rest=args.Where(a=>a!=modes[0]).ToArray();
        if(gateOnly?rest.Length!=0:
           !rest.Contains("--manual")||!rest.Any(a=>a.StartsWith("--profile=p17-body-r3-manual-test-r2-f2-",StringComparison.Ordinal))||
           !P2AWorkspace.M218BTarget)
            return null;
        Lease=LocalTrialLease.ConfirmSynthetic(run);
        if(Lease==null)return null;
        GateOnly=gateOnly;
        return rest;
    }

    static string? backup,backupSha,queue,settings;
    static string? Value(SqliteConnection connection,string scope)
    {
        using var q=connection.CreateCommand();
        q.CommandText="SELECT value FROM kv_store WHERE scope=$scope AND key='app'";
        q.Parameters.AddWithValue("$scope",scope);
        return q.ExecuteScalar() as string;
    }

    static bool Integrity(SqliteConnection connection)
    {
        using var q=connection.CreateCommand();
        q.CommandText="PRAGMA integrity_check";
        return (string?)q.ExecuteScalar()=="ok";
    }
    static string Sha(string path)
    {
        using var input=File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    internal static void Backup()
    {
        if(!Active)return;
        backup=Path.Combine(Program.Session,"m219r-rollback.sqlite");
        using(var reservation=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
        using(var source=AppDatabase.CreateConnection())
        using(var copy=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=backup,Pooling=false}.ToString()))
        {
            copy.Open();
            source.BackupDatabase(copy);
            if(!Integrity(copy))throw new IOException("Rollback database integrity failed");
            queue=QueueIdentity(Value(copy,"playback_queue"));
            settings=Value(copy,"settings");
        }
        backupSha=Sha(backup);
    }

    static string QueueIdentity(string? text)
    {
        using var json=System.Text.Json.JsonDocument.Parse(text??"{}");
        var value=json.RootElement;
        return System.Text.Json.JsonSerializer.Serialize(new{
            items=value.GetProperty("Items").EnumerateArray().Select(e=>e.GetProperty("QueueEntryId").GetGuid()),
            selected=value.GetProperty("CurrentSongKey").GetString(),
            mode=value.GetProperty("Mode").GetInt32()
        });
    }

    internal static void VerifyRollback()
    {
        if(!Active)return;
        if(backup==null||backupSha==null||Sha(backup)!=backupSha)
            throw new IOException("Trial rollback hash missing or changed");
        using var db=AppDatabase.CreateConnection();
        if(!Integrity(db)||queue!=QueueIdentity(Value(db,"playback_queue")))
            throw new IOException("Trial SQLite queue or integrity failed");
        static double Volume(string? value)
        {
            using var json=System.Text.Json.JsonDocument.Parse(value??"{}");
            return json.RootElement.GetProperty("MusicVolume").GetDouble();
        }
        if(Math.Abs(Volume(settings)-Volume(Value(db,"settings")))>1e-6)
            throw new IOException("Trial SQLite volume differs");
        Program.Log("M219R_ROLLBACK",new{backupMatch=true,sqlite=true,queue=true,volume=true});
    }
}

