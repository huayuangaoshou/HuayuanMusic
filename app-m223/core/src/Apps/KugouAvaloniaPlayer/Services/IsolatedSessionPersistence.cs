using System;
using System.Threading;
using KuGou.Net.Protocol.Session;
namespace KugouAvaloniaPlayer.Services;
/// <summary>Startup-only memory persistence admission; independent of UI, database and engine assemblies.</summary>
internal static class IsolatedSessionPersistence
{
    private static ISessionPersistence? _current;
    internal static ISessionPersistence? Current => Volatile.Read(ref _current);
    internal static void Install(ISessionPersistence memory)
    {
        if(Interlocked.CompareExchange(ref _current,memory,null)!=null)throw new InvalidOperationException("Session persistence already fixed");
    }
}
