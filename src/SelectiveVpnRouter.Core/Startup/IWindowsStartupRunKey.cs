namespace SelectiveVpnRouter.Core.Startup;

public interface IWindowsStartupRunKey
{
    string? ReadCommand(string valueName);

    void WriteCommand(string valueName, string command);

    void DeleteValue(string valueName);
}
