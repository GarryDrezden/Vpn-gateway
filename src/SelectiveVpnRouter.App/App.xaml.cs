using System.Windows;
using SelectiveVpnRouter.App.Startup;
using SelectiveVpnRouter.Core;
using SelectiveVpnRouter.Core.Portable;
using SelectiveVpnRouter.Core.Startup;
using MessageBox = System.Windows.MessageBox;

namespace SelectiveVpnRouter.App;

public partial class App : System.Windows.Application
{
    private GuiSingleInstanceCoordinator? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LayoutDebugOptions.Enabled = e.Args.Any(a => a.Equals("--layout-debug", StringComparison.OrdinalIgnoreCase));
        // Dev-only: parallel GUI instances for layout overlay debugging.
        bool singleInstanceEnabled = !LayoutDebugOptions.Enabled;
        _singleInstance = new GuiSingleInstanceCoordinator(singleInstanceEnabled);
        if (!_singleInstance.IsPrimary)
        {
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            if (args.Exception is InvalidOperationException { Message: var msg }
                && (msg.Contains("DisplayStateLabel", StringComparison.Ordinal)
                    || msg.Contains("OneWayToSource", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("TwoWay", StringComparison.OrdinalIgnoreCase)))
            {
                args.Handled = true;
                return;
            }

            MessageBox.Show(args.Exception.Message, AppBranding.ProductName);
            args.Handled = true;
        };

        ApplicationLaunchContext launch = ApplicationLaunchContext.Parse(e.Args);
        var window = new MainWindow();
        MainWindow = window;

        if (LayoutDebugOptions.Enabled)
        {
            window.ConfigureLaunch(launch, new UiSettings());
            window.Show();
            return;
        }

        UiSettings uiSettings = UiSettingsStore.Exists()
            ? UiSettingsStore.LoadOrDefault()
            : new UiSettings();
        PortableBootstrapStatus bootstrapStatus = PortableBootstrapUi.ReadStatus();
        bool bootstrapRequiresUser = StartupWindowPolicy.BootstrapRequiresUserAction(bootstrapStatus.BootstrapState);
        bool startHidden = StartupWindowPolicy.ShouldStartHidden(uiSettings.StartMinimized, bootstrapRequiresUser);
        window.ConfigureLaunch(launch, uiSettings);

        _singleInstance.StartActivationListener(() =>
        {
            if (MainWindow is MainWindow main)
            {
                main.ActivateFromSecondInstance();
            }
        });

        if (startHidden)
        {
            window.BeginHiddenStartup();
            _ = window.RunInitialStartupAsync();
            return;
        }

        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
