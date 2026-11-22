using System.Globalization;
using HuayuanMusic.Kugou.Gateway;
using KuGou.Net.Abstractions.Models;

namespace HuayuanMusic.Provider.Kugou;

// Pure, side-effect-free contract shared by the existing R5 SDK session and a
// future separately approved claim service. No HTTP, no credential ownership,
// no queue, no playback, and importantly NO receive/upgrade request.
public readonly record struct ConceptVipDailyStatus(
    DateOnly ServerDay, bool ReceivedToday, int HistoryRecords);

public enum ConceptVipEligibility
{
    AlreadyReceived,
    NotApproved,
    WrongServerDay,
    ReadyForSeparateClaimAuthorization
}

public static class ConceptVipHistory
{
    // Pure request boundary used by R5's existing HTTP handler. A valid read
    // still requires an active SDK session and the normal signing pipeline.
    // Only the monthly record GET is admitted: no claim or upgrade request.
    public static bool IsBoundedHistoryGet(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var uri=request.RequestUri;
        if(uri==null||request.Method!=HttpMethod.Get||request.Content!=null||
           request.Headers.Contains("Cookie")||uri.Scheme!="https"||uri.Port!=443||
           uri.Host!="gateway.kugou.com"||
           uri.AbsolutePath!="/youth/v1/activity/get_month_vip_record"||
           !string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Fragment))
            return false;
        int limitFields=0;
        foreach(var part in uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces=part.Split('=',2);
            if(Uri.UnescapeDataString(pieces[0])!="latest_limit")continue;
            limitFields++;
            if(pieces.Length!=2||Uri.UnescapeDataString(pieces[1])!="100")return false;
        }
        return limitFields==1;
    }

    public static ConceptVipDailyStatus Validate(
        VipReceiveHistoryResponse history,DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        if(history.Status is not (null or 1)||
           history.ErrorCode is not (null or 0)||
           history.ServerTime<=0)
            throw new InvalidDataException("概念版领取记录返回失败或缺少服务端时间");
        DateTimeOffset server;
        try{server=DateTimeOffset.FromUnixTimeSeconds(history.ServerTime);}
        catch(ArgumentOutOfRangeException e)
        {throw new InvalidDataException("概念版服务端时间超出范围",e);}
        if(Math.Abs((server-now).TotalMinutes)>10)
            throw new InvalidDataException("概念版服务端时间与本机不同步，不允许据此判断领取状态");
        if(history.Items==null||history.Items.Count>100)
            throw new InvalidDataException("概念版领取记录数量超出范围");
        var day=DateOnly.FromDateTime(server.ToOffset(TimeSpan.FromHours(8)).DateTime);
        foreach(var row in history.Items)
        {
            if(row==null||row.IsReceived is not (0 or 1)||
               !DateOnly.TryParseExact(row.Day,"yyyy-MM-dd",
                   CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
                throw new InvalidDataException("概念版领取记录条目无法验证");
        }
        bool already=history.Items.Any(x=>x.IsReceived==1&&
            x.Day==day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
        return new ConceptVipDailyStatus(day,already,history.Items.Count);
    }

    // Admission assessment only. Not an authorization source or an execution
    // path. An apparently eligible result must STILL pass the original
    // ConceptGateway independent approval and outcome-ledger checks.
    public static ConceptVipEligibility Assess(
        ConceptVipDailyStatus status,ActivityApproval? approvedActivity)
    {
        if(status.ReceivedToday)return ConceptVipEligibility.AlreadyReceived;
        if(approvedActivity==null||!approvedActivity.EligibilityConfirmed||
           approvedActivity.RequiresOfficialClientOrAd||
           string.IsNullOrWhiteSpace(approvedActivity.RulesReference)||
           string.IsNullOrWhiteSpace(approvedActivity.SourceId)||
           !approvedActivity.SourceId.All(char.IsAsciiDigit))
            return ConceptVipEligibility.NotApproved;
        if(approvedActivity.Day!=status.ServerDay)
            return ConceptVipEligibility.WrongServerDay;
        return ConceptVipEligibility.ReadyForSeparateClaimAuthorization;
    }
}
