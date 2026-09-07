#include "SelectiveVpnCallout.h"

static BOOLEAN SvrProxyProcessAlive(VOID)
{
    PEPROCESS process = NULL;
    NTSTATUS status;
    HANDLE pid;

    if (gProxyPid == 0)
    {
        return FALSE;
    }

    pid = (HANDLE)(ULONG_PTR)gProxyPid;
    status = PsLookupProcessByProcessId(pid, &process);
    if (!NT_SUCCESS(status) || process == NULL)
    {
        return FALSE;
    }

    ObDereferenceObject(process);
    return TRUE;
}

static BOOLEAN SvrAlreadyLoopbackProxy(_In_ const SOCKADDR_IN *remote)
{
    UINT32 loopback = 0x0100007F; /* 127.0.0.1 network order */
    if (remote == NULL)
    {
        return FALSE;
    }

    if (remote->sin_addr.S_un.S_addr != loopback)
    {
        return FALSE;
    }

    return RtlUshortByteSwap(remote->sin_port) == gProxyPort;
}

VOID NTAPI SvrClassifyConnectRedirect(
    _In_ const FWPS_INCOMING_VALUES *inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES *inMetaValues,
    _Inout_opt_ VOID *layerData,
    _In_opt_ const VOID *classifyContext,
    _In_ const FWPS_FILTER *filter,
    _In_ UINT64 flowContext,
    _Inout_ FWPS_CLASSIFY_OUT *classifyOut
)
{
    UNREFERENCED_PARAMETER(inFixedValues);
    UNREFERENCED_PARAMETER(layerData);
    UNREFERENCED_PARAMETER(flowContext);

    if ((classifyOut->rights & FWPS_RIGHT_ACTION_WRITE) == 0)
    {
        return;
    }

    /* Default fail-open: permit unless we successfully rewrite. */
    classifyOut->actionType = FWP_ACTION_PERMIT;

    if (!gEnabled || gProxyPid == 0 || gProxyPort == 0 || gRedirectHandle == NULL || classifyContext == NULL)
    {
        return;
    }

    UINT64 pid = 0;
    if (FWPS_IS_METADATA_FIELD_PRESENT(inMetaValues, FWPS_METADATA_FIELD_PROCESS_ID))
    {
        pid = inMetaValues->processId;
    }

    /* Never redirect the proxy's own sockets (loop prevention). */
    if (pid == 0 || pid == gProxyPid)
    {
        return;
    }

    if (!SvrProxyProcessAlive())
    {
        SvrFailOpen();
        return;
    }

    UINT64 classifyHandle = 0;
    NTSTATUS status = FwpsAcquireClassifyHandle((void *)classifyContext, 0, &classifyHandle);
    if (!NT_SUCCESS(status))
    {
        return;
    }

    FWPS_CONNECT_REQUEST *request = NULL;
    status = FwpsAcquireWritableLayerDataPointer(
        classifyHandle,
        filter->filterId,
        0,
        (PVOID *)&request,
        classifyOut);
    if (!NT_SUCCESS(status) || request == NULL)
    {
        FwpsReleaseClassifyHandle(classifyHandle);
        classifyOut->actionType = FWP_ACTION_PERMIT;
        return;
    }

    SOCKADDR_IN *remote = (SOCKADDR_IN *)&request->remoteAddressAndPort;
    if (SvrAlreadyLoopbackProxy(remote))
    {
        FwpsApplyModifiedLayerData(classifyHandle, request, FWPS_CLASSIFY_FLAG_REAUTHORIZE_IF_MODIFIED_BY_OTHERS);
        FwpsReleaseClassifyHandle(classifyHandle);
        classifyOut->actionType = FWP_ACTION_PERMIT;
        return;
    }

    SVR_REDIRECT_CONTEXT *ctx = (SVR_REDIRECT_CONTEXT *)ExAllocatePool2(
        POOL_FLAG_NON_PAGED,
        sizeof(SVR_REDIRECT_CONTEXT),
        SVR_POOL_TAG);
    if (ctx == NULL)
    {
        FwpsApplyModifiedLayerData(classifyHandle, request, 0);
        FwpsReleaseClassifyHandle(classifyHandle);
        classifyOut->actionType = FWP_ACTION_PERMIT;
        return;
    }

    ctx->RemoteAddr = remote->sin_addr.S_un.S_addr;
    ctx->RemotePort = remote->sin_port;
    ctx->Reserved = 0;

    IN_ADDR loopback;
    loopback.S_un.S_addr = 0x0100007F;
    remote->sin_family = AF_INET;
    remote->sin_addr = loopback;
    remote->sin_port = RtlUshortByteSwap(gProxyPort);

    request->localRedirectTargetPID = gProxyPid;
    request->localRedirectHandle = gRedirectHandle;
    request->localRedirectContext = ctx;
    request->localRedirectContextSize = sizeof(*ctx);

    FwpsApplyModifiedLayerData(classifyHandle, request, 0);
    FwpsReleaseClassifyHandle(classifyHandle);

    InterlockedIncrement(&gRedirects);
    classifyOut->actionType = FWP_ACTION_PERMIT;
    classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
}

NTSTATUS NTAPI SvrNotify(
    _In_ FWPS_CALLOUT_NOTIFY_TYPE notifyType,
    _In_ const GUID *filterKey,
    _Inout_ FWPS_FILTER *filter
)
{
    UNREFERENCED_PARAMETER(notifyType);
    UNREFERENCED_PARAMETER(filterKey);
    UNREFERENCED_PARAMETER(filter);
    return STATUS_SUCCESS;
}
