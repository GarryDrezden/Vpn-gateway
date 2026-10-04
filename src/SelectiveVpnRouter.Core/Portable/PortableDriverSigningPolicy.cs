namespace SelectiveVpnRouter.Core.Portable;

public static class PortableDriverSigningPolicy
{
    public static bool BlocksUnsignedInstall(string? authenticodeStatus, bool testSigningEnabled)
    {
        if (string.Equals(authenticodeStatus, "Valid", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !testSigningEnabled;
    }
}