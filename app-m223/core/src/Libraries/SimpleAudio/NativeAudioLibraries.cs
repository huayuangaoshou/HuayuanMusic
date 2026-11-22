using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Loud;

namespace SimpleAudio;

internal static class NativeAudioLibraries
{
    // Resolve BASS from the application directory, never the working directory.
#pragma warning disable CA2255 // Intentional library-wide native dependency isolation.
    [ModuleInitializer]
    internal static void Initialize()
    {
        foreach (var assembly in new[] { typeof(Bass).Assembly, typeof(BassFx).Assembly, typeof(BassLoud).Assembly }.Distinct())
            NativeLibrary.SetDllImportResolver(assembly, Resolve);
    }
#pragma warning restore CA2255

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        if (stem.StartsWith("lib", StringComparison.Ordinal)) stem = stem[3..];
        if (!stem.StartsWith("bass", StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
        var file = OperatingSystem.IsWindows() ? stem + ".dll"
            : OperatingSystem.IsMacOS() ? "lib" + stem + ".dylib" : "lib" + stem + ".so";
        return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, file));
    }
}
