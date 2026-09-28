using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace SelectiveVpnRouter.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LayoutDebugOptions.Enabled = e.Args.Any(a => a.Equals("--layout-debug", StringComparison.OrdinalIgnoreCase));
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Selective VPN Router");
            args.Handled = true;
        };

        var window = new MainWindow();
        window.Show();
    }
}
