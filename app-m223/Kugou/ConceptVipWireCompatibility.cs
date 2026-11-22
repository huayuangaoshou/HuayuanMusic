using System.Globalization;
using HuayuanMusic.Kugou.Gateway;
using KuGou.Net.Protocol.Transport;

namespace HuayuanMusic.Provider.Kugou;

public enum ConceptVipWireBlocker
{
    None,
    NotConceptDayClaim,
    MissingOrConflictingParameters,
    SourceDoesNotMatchApprovedActivity,
    RequestDayDoesNotMatchServerDay,
    RequiresOfficialClientOrAdvertisement,
    ActivityEligibilityUnverified
}

public sealed record ConceptVipWireReview(
    ConceptVipWireBlocker RouteBlocker,
    bool RouteParametersMatch,
    bool LiveExecutionAllowed,
    string Explanation);

/// <summary>
/// Inspects the EXISTING SDK's already-built request before any transport.
/// Important: the donor's RawUserApi currently hardcodes source_id=90139 and
/// DateTime.Today, ignoring the reviewed ActivityApproval and server date.
/// Never uses this review as an independent network/claim authorization.
/// </summary>
public static class ConceptVipWireCompatibility
{
    public const string SdkClaimPath="/youth/v1/recharge/receive_vip_listen_song";

    public static ConceptVipWireReview Review(
        KgRequest request, ActivityApproval? reviewed,
        DateOnly serverDay)
    {
        ArgumentNullException.ThrowIfNull(request);
        ConceptVipWireBlocker blocker;
        if(request.Method!=HttpMethod.Post || request.Path!=SdkClaimPath ||
           request.Body!=null)
            blocker=ConceptVipWireBlocker.NotConceptDayClaim;
        else if(request.Params.Count!=2 ||
                !request.Params.TryGetValue("source_id",out var source) ||
                !request.Params.TryGetValue("receive_day",out var rawDay) ||
                string.IsNullOrWhiteSpace(source) ||
                !source.All(char.IsAsciiDigit) ||
                !DateOnly.TryParseExact(rawDay,"yyyy-MM-dd",CultureInfo.InvariantCulture,
                    DateTimeStyles.None,out var sentDay))
            blocker=ConceptVipWireBlocker.MissingOrConflictingParameters;
        else if(reviewed==null || reviewed.SourceId!=source)
            blocker=ConceptVipWireBlocker.SourceDoesNotMatchApprovedActivity;
        else if(sentDay!=serverDay || reviewed.Day!=serverDay)
            blocker=ConceptVipWireBlocker.RequestDayDoesNotMatchServerDay;
        else if(reviewed.RequiresOfficialClientOrAd)
            blocker=ConceptVipWireBlocker.RequiresOfficialClientOrAdvertisement;
        else if(!reviewed.EligibilityConfirmed ||
                string.IsNullOrWhiteSpace(reviewed.RulesReference))
            blocker=ConceptVipWireBlocker.ActivityEligibilityUnverified;
        else blocker=ConceptVipWireBlocker.None;

        // Even exact request matching cannot prove that the unofficial
        // endpoint is authorized by the platform. This M2-03 build includes
        // NO live mutation dispatch permission, and the original Direct SDK
        // claim method still throws NotSupportedException.
        return new ConceptVipWireReview(blocker,blocker==ConceptVipWireBlocker.None,
            LiveExecutionAllowed:false,
            blocker==ConceptVipWireBlocker.None
                ?"参数一致，但第三方活动许可和真实领取仍未获准，继续关闭"
                :"来源/日期/规则未被证明与待发送请求一致；必须停止");
    }
}
