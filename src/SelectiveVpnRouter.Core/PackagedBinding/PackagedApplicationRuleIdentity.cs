namespace SelectiveVpnRouter.Core;

public static class PackagedApplicationRuleIdentity
{
    public static bool HasStableBinding(PackagedApplicationBinding? binding) =>
        !string.IsNullOrWhiteSpace(binding?.PackageFamilyName)
        && !string.IsNullOrWhiteSpace(binding.ApplicationId)
        && !string.IsNullOrWhiteSpace(binding.UserSid);

    public static bool AreSameLogicalApplication(PackagedApplicationBinding left, PackagedApplicationBinding right) =>
        string.Equals(left.PackageFamilyName, right.PackageFamilyName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.ApplicationId, right.ApplicationId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.UserSid, right.UserSid, StringComparison.OrdinalIgnoreCase);

    public static string FormatBindingKey(PackagedApplicationBinding binding) =>
        FormattableString.Invariant($"msix-rule:{binding.UserSid}:{binding.PackageFamilyName}:{binding.ApplicationId}");
}
