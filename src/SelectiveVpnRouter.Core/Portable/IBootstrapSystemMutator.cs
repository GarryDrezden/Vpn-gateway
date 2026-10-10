namespace SelectiveVpnRouter.Core.Portable;

internal interface IBootstrapSystemMutator
{
    void EnsureProgramData();
    void InstallOrRepairService(string portableRoot, string serviceExePath);
    void InstallOrRepairDriver(string portableRoot, string sysPath);
    void RemoveService();
    void RemoveDriver();
}
