using System.ComponentModel;
using System.Diagnostics;

namespace SelectiveVpnRouter.Core.Portable;

public sealed class PortableBootstrapProcessLauncher
{
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public PortableBootstrapProcessLauncher(Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        _startProcess = startProcess ?? Process.Start;
    }

    public static ProcessStartInfo CreateElevatedStartInfo(string normalizedPortableRoot, string arguments)
    {
        string bootstrap = Path.Combine(normalizedPortableRoot, PortableLayout.BootstrapExeName);
        return new ProcessStartInfo
        {
            FileName = bootstrap,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = normalizedPortableRoot,
        };
    }

    public PortableBootstrapLaunchResult LaunchElevatedRepair(string portableRootCandidate)
    {
        if (!PortableRootValidator.TryNormalizePortableRoot(portableRootCandidate, out string root, out string? rootError))
        {
            return new PortableBootstrapLaunchResult
            {
                Outcome = PortableBootstrapLaunchOutcome.InvalidPortableRoot,
                UserMessage = rootError ?? "Некорректная папка portable-пакета.",
            };
        }

        string bootstrap = Path.Combine(root, PortableLayout.BootstrapExeName);
        if (!File.Exists(bootstrap))
        {
            return new PortableBootstrapLaunchResult
            {
                Outcome = PortableBootstrapLaunchOutcome.BootstrapLaunchFailed,
                UserMessage = "Не найден SelectiveVpnRouter.Bootstrap.exe в папке приложения.",
            };
        }

        ProcessStartInfo psi = CreateElevatedStartInfo(root, PortableBootstrapCommandLine.BuildRepairArguments(root));
        int? processExit = null;
        try
        {
            using Process? process = _startProcess(psi);
            if (process is null)
            {
                return new PortableBootstrapLaunchResult
                {
                    Outcome = PortableBootstrapLaunchOutcome.BootstrapLaunchFailed,
                    UserMessage = "Не удалось запустить Bootstrap (Process.Start вернул null).",
                };
            }

            process.WaitForExit();
            processExit = process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new PortableBootstrapLaunchResult
            {
                Outcome = PortableBootstrapLaunchOutcome.ElevationCancelled,
                UserMessage = "Подготовка отменена пользователем.",
            };
        }
        catch (Exception ex)
        {
            return new PortableBootstrapLaunchResult
            {
                Outcome = PortableBootstrapLaunchOutcome.BootstrapLaunchFailed,
                UserMessage = "Не удалось запустить Bootstrap: " + ex.Message,
            };
        }

        return InterpretPostLaunch(root, processExit);
    }

    public PortableBootstrapLaunchResult LaunchElevatedRemove(string portableRootCandidate)
    {
        if (!PortableRootValidator.TryNormalizePortableRoot(portableRootCandidate, out string root, out string? rootError))
        {
            return new PortableBootstrapLaunchResult
            {
                Outcome = PortableBootstrapLaunchOutcome.InvalidPortableRoot,
                UserMessage = rootError ?? "Некорректная папка portable-пакета.",
            };
        }

        if (!File.Exists(Path.Combine(root, PortableLayout.BootstrapExeName)))
        {
            return new PortableBootstrapLaunchResult
            {
                Outcome = PortableBootstrapLaunchOutcome.BootstrapLaunchFailed,
                UserMessage = "Не найден SelectiveVpnRouter.Bootstrap.exe в папке приложения.",
            };
        }

        ProcessStartInfo psi = CreateElevatedStartInfo(root, PortableBootstrapCommandLine.BuildRemoveArguments(root));
        int? processExit = null;
        try
        {
            using Process? process = _startProcess(psi);
            if (process is null)
            {
                return new PortableBootstrapLaunchResult
                {
                    Outcome = PortableBootstrapLaunchOutcome.BootstrapLaunchFailed,
                    UserMessage = "Не удалось запустить Bootstrap remove.",
                };
            }

            process.WaitForExit();
            processExit = process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new PortableBootstrapLaunchResult
            {
                Outcome = PortableBootstrapLaunchOutcome.ElevationCancelled,
                UserMessage = "Удаление отменено пользователем.",
            };
        }
        catch (Exception ex)
        {
            return new PortableBootstrapLaunchResult
            {
                Outcome = PortableBootstrapLaunchOutcome.BootstrapLaunchFailed,
                UserMessage = "Не удалось запустить Bootstrap remove: " + ex.Message,
            };
        }

        return InterpretPostLaunch(root, processExit);
    }

    public PortableBootstrapLaunchResult InterpretPostLaunch(string normalizedRoot, int? processExit)
    {
        PortableBootstrapStatus status = new PortableBootstrapEngine().GetStatus(normalizedRoot);
        if (status.BootstrapState == PortableBootstrapState.Ready)
        {
            return new PortableBootstrapLaunchResult
            {
                Outcome = PortableBootstrapLaunchOutcome.Ready,
                UserMessage = status.Message,
                Status = status,
                ProcessExitCode = processExit,
            };
        }

        PortableBootstrapLastResult? last = PortableBootstrapResultIO.TryReadLastResult();
        if (status.DriverSigningBlocked
            || last?.Status?.DriverSigningBlocked == true
            || last?.ExitCode == PortableBootstrapExitCodes.DriverSigningBlocked)
        {
            string msg = !string.IsNullOrWhiteSpace(last?.Message) ? last.Message : status.Message;
            return new PortableBootstrapLaunchResult
            {
                Outcome = PortableBootstrapLaunchOutcome.DriverSigningBlocked,
                UserMessage = msg,
                Status = last?.Status ?? status,
                ProcessExitCode = processExit,
            };
        }

        if (last?.ExitCode == PortableBootstrapExitCodes.IpcFailed)
        {
            return new PortableBootstrapLaunchResult
            {
                Outcome = PortableBootstrapLaunchOutcome.IpcFailed,
                UserMessage = string.IsNullOrWhiteSpace(last.Message)
                    ? "Служба не ответила на IPC после repair."
                    : last.Message,
                Status = status,
                ProcessExitCode = processExit,
            };
        }

        if (last?.ExitCode == PortableBootstrapExitCodes.ElevationRequired)
        {
            return new PortableBootstrapLaunchResult
            {
                Outcome = PortableBootstrapLaunchOutcome.ElevationRequired,
                UserMessage = "Требуются права администратора. Подтвердите запрос UAC.",
                Status = status,
                ProcessExitCode = processExit,
            };
        }

        string detail = status.Message;
        if (!string.IsNullOrWhiteSpace(last?.Message))
        {
            detail = last.Message;
        }

        return new PortableBootstrapLaunchResult
        {
            Outcome = PortableBootstrapLaunchOutcome.RepairIncomplete,
            UserMessage = detail,
            Status = status,
            ProcessExitCode = processExit,
        };
    }
}