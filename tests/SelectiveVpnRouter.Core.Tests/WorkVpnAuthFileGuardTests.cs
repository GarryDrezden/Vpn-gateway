using SelectiveVpnRouter.Core;
using Xunit;

namespace SelectiveVpnRouter.Core.Tests;

public class WorkVpnAuthFileGuardTests
{
    [Fact]
    public void Exists_until_deleted_once()
    {
        string path = Path.Combine(Path.GetTempPath(), "work-auth-test-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "user\npass");
        var guard = new WorkVpnAuthFileGuard(path);
        Assert.True(guard.Exists);
        guard.DeleteOnce();
        Assert.False(guard.Exists);
        guard.DeleteOnce();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Dispose_deletes_file()
    {
        string path = Path.Combine(Path.GetTempPath(), "work-auth-test-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "user\npass");
        using (var guard = new WorkVpnAuthFileGuard(path))
        {
            Assert.True(guard.Exists);
        }

        Assert.False(File.Exists(path));
    }
}