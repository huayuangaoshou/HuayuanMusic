using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Avalonia.Threading;

namespace HuayuanPreview;

internal static class CrashEvidence
{
    private const string LastSessionFile = "last-session.txt";
    private const string CleanMarker = "session-clean-exit";
    private const uint MiniDumpNormal = 0;
    private static int dumpAttempted;

    internal static void StartSession(string root, string session)
    {
        try
        {
            Directory.CreateDirectory(session);
            var pointer = Path.Combine(root, LastSessionFile);
            if (File.Exists(pointer) && new FileInfo(pointer).Length <= 64)
            {
                var previous = File.ReadAllText(pointer).Trim();
                if (Guid.TryParseExact(previous, "N", out var id) &&
                    !File.Exists(Path.Combine(root, id.ToString("N"), CleanMarker)))
                    Log("PREVIOUS_SESSION_ABNORMAL_EXIT", new { previous = id.ToString("N") });
            }
            var pending = Path.Combine(session, LastSessionFile);
            File.WriteAllText(pending, Path.GetFileName(session));
            File.Move(pending, pointer, overwrite: true);
        }
        catch (Exception error)
        {
            Log("SESSION_SENTINEL_FAILED", new { phase = "start", type = error.GetType().Name });
        }
    }

    internal static void MarkCleanExit(string session)
    {
        try
        {
            File.WriteAllText(Path.Combine(session, CleanMarker), "");
            Log("SESSION_CLEAN_EXIT", new { });
        }
        catch (Exception error)
        {
            Log("SESSION_SENTINEL_FAILED", new { phase = "exit", type = error.GetType().Name });
        }
    }

    internal static void AttachHandlers(string session)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Record("UNHANDLED_CRASH", args.ExceptionObject as Exception);
            if (args.IsTerminating)
                TryWriteDump(session);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            Record("UNOBSERVED_TASK", args.Exception.GetBaseException());
        };
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            Record("UI_THREAD_UNHANDLED_EXCEPTION", args.Exception);
            TryWriteDump(session);
        };
    }

    private static void Record(string kind, Exception? error)
    {
        try { Log(kind, new { type = error?.GetType().FullName, stack = error?.StackTrace }); }
        catch { /* Diagnostics must never replace the exception being reported. */ }
    }

    private static void TryWriteDump(string session)
    {
        if (!OperatingSystem.IsWindows() || Interlocked.Exchange(ref dumpAttempted, 1) != 0)
            return;

        var path = Path.Combine(session, "crash.dmp");
        var partial = path + ".tmp";
        try
        {
            using var process = Process.GetCurrentProcess();
            using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!MiniDumpWriteDump(process.SafeHandle, (uint)process.Id, output.SafeFileHandle,
                        MiniDumpNormal, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
                {
                    Log("CRASH_DUMP_FAILED", new { error = Marshal.GetLastWin32Error() });
                    return;
                }
            }
            File.Move(partial, path);
            Log("CRASH_DUMP_WRITTEN", new { file = "crash.dmp" });
        }
        catch (Exception error)
        {
            Log("CRASH_DUMP_FAILED", new { type = error.GetType().Name });
        }
        finally
        {
            try { File.Delete(partial); }
            catch { /* Leave the original failure behavior intact. */ }
        }
    }

    private static void Log(string kind, object detail)
    {
        try { Program.Log(kind, detail); }
        catch { /* Diagnostic failures must not escape a crash callback. */ }
    }

    [DllImport("dbghelp.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(SafeProcessHandle process, uint processId,
        SafeFileHandle file, uint dumpType, IntPtr exception, IntPtr userStream, IntPtr callback);
}
