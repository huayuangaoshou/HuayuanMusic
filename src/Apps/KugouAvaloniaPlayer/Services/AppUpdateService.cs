using System.Threading.Tasks;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.Services;

/// <summary>
/// P1 safety gate: no calls to KA Music's upstream update source.
/// Independent private update distribution is NOT configured.
/// </summary>
public interface IAppUpdateService
{
    Task<UpdateCheckResult> CheckForUpdatesAsync(bool showNoUpdateToast = false);
}

public sealed class AppUpdateService : IAppUpdateService
{
    public Task<UpdateCheckResult> CheckForUpdatesAsync(bool showNoUpdateToast = false)
        => Task.FromResult(new UpdateCheckResult(
            UpdateCheckState.Unsupported,
            "Huayuan Music P1 开发预览版：自动更新已禁用，避免接入原版更新渠道。"));
}
