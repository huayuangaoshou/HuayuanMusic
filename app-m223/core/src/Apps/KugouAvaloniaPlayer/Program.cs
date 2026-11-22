using System;
using Avalonia;
#if KUGOU_WINDOWS
using KugouAvaloniaPlayer.Services.SystemMediaSession;
#endif
using KugouAvaloniaPlayer.Services.Startup;
using Serilog;

namespace KugouAvaloniaPlayer;

internal sealed class Program
{
    private static StartupInstanceCoordinator? StartupCoordinator;

    [STAThread]
    public static void Main(string[] args)
    {
        // Must precede logs, user paths, instance IPC, identity, Avalonia and DI.
        // Only separately admitted offline modes can run; unrestricted startup remains refused.
        if(args.Length>0 && args[0]=="--p15-offline-window")
        {
            Environment.ExitCode=NormalStartupGate.RunGui(args, Console.Out, (name,ticket,selftest,writer)=>OfflineGuiApplication.Run(name,ticket,selftest,writer,BuildAvaloniaApp));
            return;
        }
        if(args.Length>0 && args[0]=="--p15a-r2-infrastructure")
        {
            Environment.ExitCode=NormalStartupGate.RunInfrastructure(args, Console.Out, OfflineInfrastructureProbe.Run);
            return;
        }
        Environment.ExitCode = NormalStartupGate.Run(args, Console.Out);
    }

    private static void RunUnapprovedLegacyStartup(string[] args)
    {
        NormalStartupGate.DemandGuiAuthorization();
        StartupCoordinator = new StartupInstanceCoordinator();
        CrashReporting.ConfigureLogging();
        CrashReporting.RegisterGlobalHandlers();

        var launchResult = StartupCoordinator.TryAcquireOrForward(args);
        if (launchResult == StartupInstanceLaunchResult.ForwardedToPrimary)
        {
            Log.CloseAndFlush();
            return;
        }

#if KUGOU_WINDOWS
        WindowsAppIdentity.Register();
#endif
        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "桌面程序启动或运行期间发生致命异常");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    internal static void ShutdownStartupCoordinator()
    {
        StartupCoordinator?.Dispose();
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

#if KUGOU_MACOS
        builder = builder.With(new AvaloniaNativePlatformOptions
        {
            RenderingMode =
            [
                AvaloniaNativeRenderingMode.OpenGl,
                AvaloniaNativeRenderingMode.Metal,
                AvaloniaNativeRenderingMode.Software,
            ],
        });
#elif KUGOU_WINDOWS
        builder = builder.With(new Win32PlatformOptions
        {
            RenderingMode =
            [
                Win32RenderingMode.AngleEgl,
                Win32RenderingMode.Wgl,
                Win32RenderingMode.Software,
            ],
        });
#elif KUGOU_LINUX
        builder = builder.With(new X11PlatformOptions
        {
            RenderingMode =
            [
                X11RenderingMode.Egl,
                X11RenderingMode.Glx,
                X11RenderingMode.Software,
            ],
        });
#endif

        return builder;
    }
}
