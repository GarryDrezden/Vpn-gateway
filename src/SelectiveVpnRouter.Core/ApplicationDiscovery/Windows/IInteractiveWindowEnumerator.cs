namespace SelectiveVpnRouter.Core.ApplicationDiscovery.Windows;

public interface IInteractiveWindowEnumerator
{
    IReadOnlyList<InteractiveWindowSnapshot> EnumerateVisibleTopLevelWindows();
}