using System.Runtime.CompilerServices;
using HuayuanMusic.Kugou.Gateway;
using KuGou.Net.Abstractions.Models;

[assembly: InternalsVisibleTo("M2OfflineTests")]

namespace HuayuanMusic.Provider.Kugou;

// Share the original ConceptGateway's four distinct acknowledgement outcomes.
// A positive transport acknowledgement does NOT prove VIP rights or song access.
public static class ConceptVipClaimReceiptPolicy
{
    public static ClaimAcknowledgement Decode(OneDayVipModel? receipt)
    {
        if(receipt?.Status==1 && receipt.ErrorCode is null or 0 &&
           receipt.AdVipNum>0)
            return ClaimAcknowledgement.Confirmed;
        if(receipt?.Status==0 && receipt.ErrorCode is >0)
            return ClaimAcknowledgement.Rejected;
        return ClaimAcknowledgement.Unknown;
    }

    public static bool? IsActive(UserVipResponse? response)
    {
        if(response==null || response.Status!=1 ||
           response.ErrorCode is not (null or 0))
            return null;
        return response.IsVip==1||response.IsConceptVip||response.IsSuperVip;
    }
}

// Never used by a live GUI or DirectSdkGateway: the only implementation is an
// offline fixture using the SAME synthetic KgSessionManager and UserClient.
// Do not accept a real HttpClient, credential, platform permit or login token.
internal interface IConceptVipOfflineFixture
{
    string AccountKey {get;}
    bool LoggedIn {get;}
    Task<VipReceiveHistoryResponse?> ReadHistoryAsync(CancellationToken ct);
    Task<OneDayVipModel?> ReceiveAsync(CancellationToken ct);
    Task<UserVipResponse?> ReadEntitlementAsync(CancellationToken ct);
}

internal sealed record ConceptVipOfflineRun(
    ClaimAcknowledgement Outcome,
    bool DispatchAttempted,
    bool? ServerRecordedToday,
    bool? VipActive,
    bool FromExistingLedger);

// Deliberately internal: no production mutation entry point, transport, timer
// or loose user-supplied callback is published by this research candidate.
// The test-only SDK adapter lives exclusively in M2OfflineTests.
internal static class ConceptVipOfflineClaimCoordinator
{
    public static async Task<ConceptVipOfflineRun> ExecuteFixtureAsync(
        IConceptVipOfflineFixture fixture,
        ConceptVipClaimLedger ledger,
        ActivityApproval? activity,
        bool explicitUserClick,
        bool separatePermissionVerified,
        DateTimeOffset now,
        CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(ledger);
        if(!fixture.LoggedIn)
            throw new UnauthorizedAccessException("Offline fixture must have one signed-in synthetic SDK session");
        // Already canceled: refuse before touching the day slot. A cancellation
        // occurring AFTER durable reservation still leaves UNKNOWN and no retry.
        ct.ThrowIfCancellationRequested();

        // No mutation before the existing read-only history and day check.
        var history=await fixture.ReadHistoryAsync(ct) ??
            throw new InvalidDataException("Claim history missing");
        var status=ConceptVipHistory.Validate(history,now);
        ct.ThrowIfCancellationRequested();
        var reservation=ledger.Begin(fixture.AccountKey,status,activity,
            explicitUserClick,separatePermissionVerified);

        if(!reservation.MayDispatch)
            return new ConceptVipOfflineRun(reservation.Entry.Outcome,false,
                status.ReceivedToday,null,!status.ReceivedToday);

        // Reservation is already durably UNKNOWN, even if cancellation or a
        // simulated crash interrupts the next step.
        ClaimAcknowledgement outcome=ClaimAcknowledgement.Unknown;
        try
        {
            ct.ThrowIfCancellationRequested();
            var receipt=await fixture.ReceiveAsync(ct);
            outcome=ConceptVipClaimReceiptPolicy.Decode(receipt);
        }
        catch(Exception e)when(e is OperationCanceledException or
            TimeoutException or HttpRequestException or IOException)
        {
            outcome=ClaimAcknowledgement.Unknown;
        }
        // Failure to persist completion leaves UNKNOWN, which forbids retry.
        ledger.Finish(reservation,outcome);

        // Verify separately; no evidence in either read grants a second POST.
        // Failure of the read cannot silently upgrade UNKNOWN or claim rights.
        bool? serverRecorded=null;
        try
        {
            var after=await fixture.ReadHistoryAsync(ct);
            if(after!=null)
                serverRecorded=ConceptVipHistory.Validate(after,now).ReceivedToday;
        }
        catch(Exception e)when(e is OperationCanceledException or TimeoutException or
            HttpRequestException or IOException or InvalidDataException)
        {
            serverRecorded=null;
        }

        bool? vipActive=null;
        try
        {
            vipActive=ConceptVipClaimReceiptPolicy.IsActive(
                await fixture.ReadEntitlementAsync(ct));
        }
        catch(Exception e)when(e is OperationCanceledException or TimeoutException or
            HttpRequestException or IOException or InvalidDataException)
        {
            vipActive=null;
        }

        return new ConceptVipOfflineRun(outcome,true,serverRecorded,
            vipActive,false);
    }
}
