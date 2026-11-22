using HuayuanMusic.Kugou.Gateway;
using KuGou.Net.Abstractions.Models;

namespace HuayuanMusic.Provider.Kugou;

// Shared contract for the existing licensed/approved adapter and the isolated
// direct upstream SDK compatibility adapter. The provider owns the ONLY queue grant.
public interface IRealSdkGateway : IKugouGateway
{
    bool LoggedIn { get; }
    string Alias { get; }
    event Action? Revoked;
    Task<QrChallenge> BeginQrAsync(CancellationToken ct);
    Task<QrLoginStatus> PollQrAsync(CancellationToken ct);
    Task CancelLoginAsync(CancellationToken ct);
    Task<VipReceiveHistoryResponse> ReadClaimHistoryAsync(CancellationToken ct);
    Task<ClaimReceipt> ClaimTodayAsync(bool explicitUserClick,CancellationToken ct);
    bool Allows(Uri handle);
    Task<OpenedAudio> OpenAsync(Uri handle,CancellationToken ct);
    void Release(Uri handle);
}
