using System.Text;
using SelectiveVpnRouter.Core;

namespace SelectiveVpnRouter.Service;

internal static class ServiceFatalLogger
{
    internal static void Write(string heading, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            var sb = new StringBuilder();
            sb.Append(DateTimeOffset.Now.ToString("o"));
            sb.Append(" FATAL ");
            sb.Append(heading);
            sb.Append(" pid=");
            sb.Append(Environment.ProcessId);
            sb.Append(" hostStopping=");
            sb.Append(ServiceRuntimeContext.HostStopping);
            sb.Append(" ipcMethod=");
            sb.Append(ServiceRuntimeContext.ActiveIpcMethod ?? "(none)");
            sb.Append(" diagnostic=");
            sb.Append(ServiceRuntimeContext.ActiveDiagnosticName ?? "(none)");
            sb.AppendLine();
            if (ex is not null)
            {
                sb.AppendLine(ex.ToString());
            }

            File.AppendAllText(Path.Combine(AppPaths.LogDirectory, "service.log"), sb.ToString());
        }
        catch
        {
        }
    }
}