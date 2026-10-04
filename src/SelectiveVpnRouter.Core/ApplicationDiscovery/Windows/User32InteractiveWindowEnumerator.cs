using System.Runtime.InteropServices;
using System.Text;

namespace SelectiveVpnRouter.Core.ApplicationDiscovery.Windows;

public sealed class User32InteractiveWindowEnumerator : IInteractiveWindowEnumerator
{
    private const int GwlExStyle = -20;
    private const int GwOwner = 4;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExAppWindow = 0x00040000;
    private const uint WsExNoActivate = 0x08000000;

    private static readonly HashSet<string> IgnoredClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "DV2ControlHost",
        "MsgrIMEWindowClass",
        "Windows.UI.Core.CoreWindow",
    };

    public IReadOnlyList<InteractiveWindowSnapshot> EnumerateVisibleTopLevelWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        List<InteractiveWindowSnapshot> results = [];
        EnumWindows((hwnd, _) =>
        {
            if (!IsEligibleTopLevelWindow(hwnd, out int pid, out string? title, out string? className))
            {
                return true;
            }

            results.Add(new InteractiveWindowSnapshot(pid, title, className));
            return true;
        }, IntPtr.Zero);

        return results;
    }

    internal static bool IsEligibleTopLevelWindow(IntPtr hwnd, out int processId, out string? title, out string? className)
    {
        processId = 0;
        title = null;
        className = null;

        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd))
        {
            return false;
        }

        if (GetWindow(hwnd, GwOwner) != IntPtr.Zero)
        {
            return false;
        }

        uint exStyle = (uint)GetWindowLongPtr(hwnd, GwlExStyle);
        if ((exStyle & WsExToolWindow) != 0 && (exStyle & WsExAppWindow) == 0)
        {
            return false;
        }

        if ((exStyle & WsExNoActivate) != 0)
        {
            return false;
        }

        StringBuilder classBuffer = new(256);
        _ = GetClassName(hwnd, classBuffer, classBuffer.Capacity);
        className = classBuffer.ToString();
        if (IgnoredClassNames.Contains(className))
        {
            return false;
        }

        StringBuilder titleBuffer = new(512);
        _ = GetWindowText(hwnd, titleBuffer, titleBuffer.Capacity);
        title = titleBuffer.ToString().Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        _ = GetWindowThreadProcessId(hwnd, out uint pidUnsigned);
        processId = unchecked((int)pidUnsigned);
        return processId > 0;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}