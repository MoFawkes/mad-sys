using System.Runtime.CompilerServices;
using AqiClock.App.Services;
using Velopack;

namespace AqiClock.App;

internal static class Bootstrap
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => { ProtocolRegistration.Register(); TaskbarRegistration.UpdateLaunchPath(); })
            .OnAfterUpdateFastCallback(_ => { ProtocolRegistration.Register(); TaskbarRegistration.UpdateLaunchPath(); })
            .OnBeforeUninstallFastCallback(_ => { ProtocolRegistration.Unregister(); TaskbarRegistration.Uninstall(); })
            .Run();
        RunApplication();
    }

    // Fast-exit hooks must complete before the JIT resolves WPF application types.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunApplication()
    {
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
