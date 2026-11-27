using System.Globalization;
using KugouAvaloniaPlayer.Services;
using HuayuanMusic.Provider.Kugou;

namespace HuayuanPreview;

/// <summary>
/// 每日自动领取酷狗概念版 VIP（听歌领 VIP 活动，source_id=90139）。
/// 对齐借鉴项目 core VipEntitlementService 的"登录即自动领当日 VIP"行为：用户本人账号、每日一次。
///
/// 触发点：连接 SDK 成功后、扫码登录成功后，延时 5 秒 fire-and-forget 调用 ClaimTodayAsync(true)。
/// 防重：本地持久化 {accountKey: lastAttemptDay}；当日已尝试（含 Unknown/Rejected）不再调用，次日恢复。
/// 静默：整体 try/catch，仅落 VIP_CLAIM 结构化日志；零 UI、零播放影响。
/// </summary>
internal static class ConceptVipAutoClaim
{
    // 与 KugouRiskSettings 同层：复用 AppSqliteStore 原子 KV 存储，不存账号密钥或媒体密钥。
    const string Store = "dlc_vip_claim";
    const string ScopeKeyPrefix = "kugou-concept-claim:";

    /// <summary>连接/登录成功后触发。fire-and-forget，绝不抛异常、绝不影响播放。</summary>
    internal static void ScheduleAfterLoginAsync(CorePlaybackAdapter adapter)
    {
        _ = RunAsync(adapter);
    }

    static async Task RunAsync(CorePlaybackAdapter adapter)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            var key = adapter.Kugou.DirectAccountCacheKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                Program.Log("VIP_CLAIM", new { state = "skip", reason = "no-account-key" });
                return;
            }
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);
            var persisted = ReadAttemptDay(key);
            if (persisted == today)
            {
                Program.Log("VIP_CLAIM", new { state = "skip", reason = "already-attempted-today", serverDay = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
                return;
            }
            var receipt = await adapter.Kugou.ClaimTodayAsync(true);
            // 无论成功/已领/被拒/未知，当日都不再二次 POST。
            WriteAttemptDay(key, today);
            Program.Log("VIP_CLAIM", new
            {
                state = receipt.State.ToString(),
                serverDay = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                active = receipt.Entitlement.Active,
                product = receipt.Entitlement.Product,
            });
        }
        catch (OperationCanceledException) { Program.Log("VIP_CLAIM", new { state = "cancelled" }); }
        catch (Exception error)
        {
            Program.Log("VIP_CLAIM", new { state = "error", type = error.GetType().Name });
        }
    }

    static DateOnly? ReadAttemptDay(string accountKey)
    {
        try
        {
            var raw = AppSqliteStore.LoadValue(Store, ScopeKeyPrefix + accountKey);
            return DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : null;
        }
        catch { return null; }
    }

    static void WriteAttemptDay(string accountKey, DateOnly day)
    {
        try
        {
            AppSqliteStore.SaveValue(Store, ScopeKeyPrefix + accountKey,
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        catch (Exception error)
        {
            Program.Log("VIP_CLAIM", new { state = "persist-failed", type = error.GetType().Name });
        }
    }
}
