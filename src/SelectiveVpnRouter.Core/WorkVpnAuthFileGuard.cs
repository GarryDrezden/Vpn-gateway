namespace SelectiveVpnRouter.Core;

/// <summary>
/// Tracks a short-lived auth-user-pass file; delete is idempotent.
/// </summary>
public sealed class WorkVpnAuthFileGuard : IDisposable
{
    private bool _deleted;

    public WorkVpnAuthFileGuard(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public bool Exists => !_deleted && File.Exists(Path);

    public void DeleteOnce()
    {
        if (_deleted)
        {
            return;
        }

        _deleted = true;
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch (Exception)
        {
        }
    }

    public void Dispose() => DeleteOnce();
}
