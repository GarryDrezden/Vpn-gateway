namespace SelectiveVpnRouter.Core;

public interface IPackagedApplicationPathResolver
{
    PackagedApplicationPathResolveResult Resolve(PackagedApplicationBinding binding);
}
