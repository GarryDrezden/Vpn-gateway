namespace SelectiveVpnRouter.Core.Startup;

/// <summary>
/// Result of attempting to become the primary GUI instance (testable decision surface).
/// </summary>
public sealed record GuiSingleInstanceAttempt(bool IsPrimary, bool ShouldSignalExistingInstance);

public static class GuiSingleInstancePolicy
{
    public static GuiSingleInstanceAttempt Evaluate(bool singleInstanceEnabled, bool acquiredPrimaryMutex)
    {
        if (!singleInstanceEnabled)
        {
            return new GuiSingleInstanceAttempt(IsPrimary: true, ShouldSignalExistingInstance: false);
        }

        if (acquiredPrimaryMutex)
        {
            return new GuiSingleInstanceAttempt(IsPrimary: true, ShouldSignalExistingInstance: false);
        }

        return new GuiSingleInstanceAttempt(IsPrimary: false, ShouldSignalExistingInstance: true);
    }
}
