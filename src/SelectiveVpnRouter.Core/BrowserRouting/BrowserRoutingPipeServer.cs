using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SelectiveVpnRouter.Core.BrowserRouting;

/// <summary>
/// Local named pipe server of the browser routing endpoint. One request per connection:
/// read one frame, answer one frame, disconnect. No TCP, no network port, no impersonation.
/// </summary>
public sealed class BrowserRoutingPipeServer(
    string pipeName,
    BrowserRoutingIpcDispatcher dispatcher,
    Action<string> log)
{
    public const int MaxConcurrentClients = 4;
    public static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Pipe DACL:
    /// SYSTEM, Administrators and the creating identity: full control (needed to create instances);
    /// INTERACTIVE users: read/write only — they can call the endpoint but cannot create a pipe instance;
    /// NETWORK: denied. Everyone/Anonymous/Authenticated Users are not granted anything.
    /// </summary>
    public static PipeSecurity CreatePipeSecurity(SecurityIdentifier creator)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(admins, PipeAccessRights.FullControl, AccessControlType.Allow));
        if (!creator.Equals(system) && !creator.Equals(admins))
            security.AddAccessRule(new PipeAccessRule(creator, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return security;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var creator = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No user SID.");
        var security = CreatePipeSecurity(creator);
        using var slots = new SemaphoreSlim(MaxConcurrentClients, MaxConcurrentClients);
        var first = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            NamedPipeServerStream pipe;
            try
            {
                // FirstPipeInstance: if anyone already owns this name, refuse to serve instead of sharing it.
                pipe = NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.InOut,
                    MaxConcurrentClients,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None),
                    inBufferSize: 0,
                    outBufferSize: 0,
                    security);
            }
            catch
            {
                slots.Release();
                throw;
            }
            first = false;

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                slots.Release();
                throw;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await ServeAsync(pipe, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    slots.Release();
                }
            }, CancellationToken.None);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ClientTimeout);
        try
        {
            var header = new byte[4];
            if (!await ReadExactlyAsync(pipe, header, timeout.Token).ConfigureAwait(false))
                return;
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            BrowserRoutingIpcDispatcher.Outcome outcome;
            if (length is <= 0 or > BrowserRoutingIpcProtocol.MaxRequestBytes)
            {
                outcome = dispatcher.Dispatch(ReadOnlySpan<byte>.Empty);
            }
            else
            {
                var body = new byte[length];
                if (!await ReadExactlyAsync(pipe, body, timeout.Token).ConfigureAwait(false))
                    return;
                outcome = dispatcher.Dispatch(body);
            }

            var responseHeader = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(responseHeader, outcome.Response.Length);
            await pipe.WriteAsync(responseHeader, timeout.Token).ConfigureAwait(false);
            await pipe.WriteAsync(outcome.Response, timeout.Token).ConfigureAwait(false);
            await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            pipe.WaitForPipeDrain();
            log($"browser-routing {outcome.Method}: {outcome.Result}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            log("browser-routing client timed out");
        }
        catch (IOException)
        {
            log("browser-routing client disconnected");
        }
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return false;
            offset += read;
        }
        return true;
    }
}
