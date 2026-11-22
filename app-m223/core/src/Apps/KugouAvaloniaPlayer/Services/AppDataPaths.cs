using System;
using System.IO;

namespace KugouAvaloniaPlayer.Services;

/// <summary>
/// Huayuan Music P1 MUST NOT read, migrate or mutate KA Music user data.
/// These roots are intentionally separate from the upstream 'kugou' folders.
/// </summary>
public static class AppDataPaths
{
    public static string RoamingRoot => Startup.OfflineStartupProfile.DataRoot(Environment.SpecialFolder.ApplicationData);
    public static string LocalRoot => Startup.OfflineStartupProfile.DataRoot(Environment.SpecialFolder.LocalApplicationData);
}
