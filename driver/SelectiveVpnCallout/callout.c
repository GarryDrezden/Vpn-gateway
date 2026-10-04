#include "SelectiveVpnCallout.h"

static void SvrApplyModifiedLayerDataTracked(
    _In_ UINT64 classifyHandle,
    _In_ FWPS_CONNECT_REQUEST *request,
    _In_ UINT32 flags,
    _In_ BOOLEAN countSuccessAsRedirect)
{
    InterlockedIncrement(&gRedirectAttempts);
    FwpsApplyModifiedLayerData(classifyHandle, request, flags);
    /* WDK 10.0.26100: FwpsApplyModifiedLayerData0 returns void (no NTSTATUS). */
    InterlockedExchange(&gLastRedirectApplyStatus, (LONG)STATUS_SUCCESS);
    if (countSuccessAsRedirect)
    {
        InterlockedIncrement(&gRedirectApplySuccess);
        InterlockedIncrement(&gRedirects);
    }
}

/* Writable ALE connect data was acquired; finalize without redirect (required before release). */
static void SvrPermitWritableConnectWithoutRedirect(
    _In_ UINT64 classifyHandle,
    _In_ FWPS_CONNECT_REQUEST *request)
{
    SvrApplyModifiedLayerDataTracked(
        classifyHandle,
        request,
        FWPS_CLASSIFY_FLAG_REAUTHORIZE_IF_MODIFIED_BY_OTHERS,
        FALSE);
    FwpsReleaseClassifyHandle(classifyHandle);
}

static VOID SvrCaptureRuntimeAppId(
    _In_ const FWPS_INCOMING_VALUES *inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES *inMetaValues,
    _In_ const FWPS_FILTER *filter,
    _Inout_ FWPS_CLASSIFY_OUT *classifyOut
)
{
    KIRQL oldIrql;
    UINT64 pid = 0;

    if (FWPS_IS_METADATA_FIELD_PRESENT(inMetaValues, FWPS_METADATA_FIELD_PROCESS_ID))
    {
        pid = inMetaValues->processId;
    }

    KeAcquireSpinLock(&gRuntimeCaptureLock, &oldIrql);
    InterlockedIncrement(&gRuntimeCaptureCount);
    gRuntimeCapturePid = pid;
    gRuntimeCaptureFilterId = filter != NULL ? filter->filterId : 0;
    gRuntimeCaptureRights = classifyOut != NULL ? (ULONG)classifyOut->rights : 0;
    gRuntimeAppIdPresent = 0;
    gRuntimeAppIdByteLength = 0;
    gRuntimeAppIdValueType = 0;
    RtlZeroMemory(gRuntimeAppIdText, sizeof(gRuntimeAppIdText));

    if (inFixedValues != NULL && inFixedValues->incomingValue != NULL)
    {
        const FWPS_INCOMING_VALUE0 *incoming =
            &inFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_ALE_APP_ID];
        gRuntimeAppIdValueType = (ULONG)incoming->value.type;
        if (incoming->value.type == FWP_BYTE_BLOB_TYPE && incoming->value.byteBlob != NULL)
        {
            FWP_BYTE_BLOB *blob = (FWP_BYTE_BLOB *)incoming->value.byteBlob;
            gRuntimeAppIdByteLength = blob->size;
            gRuntimeAppIdPresent = 1;
            if (blob->data != NULL && blob->size >= sizeof(WCHAR))
            {
                UINT32 copyBytes = blob->size;
                UINT32 maxBytes = (SVR_RUNTIME_APP_ID_TEXT_CHARS - 1) * sizeof(WCHAR);
                if (copyBytes > maxBytes)
                {
                    copyBytes = maxBytes;
                }

                copyBytes &= ~(sizeof(WCHAR) - 1);
                if (copyBytes > 0)
                {
                    RtlCopyMemory(gRuntimeAppIdText, blob->data, copyBytes);
                    gRuntimeAppIdText[copyBytes / sizeof(WCHAR)] = L'\0';
                }
            }
        }
    }

    KeReleaseSpinLock(&gRuntimeCaptureLock, oldIrql);

    if (classifyOut != NULL)
    {
        classifyOut->actionType = FWP_ACTION_PERMIT;
    }
}

static BOOLEAN SvrIsIpv4LoopbackDestination(_In_ const SOCKADDR_IN *remote)
{
    if (remote == NULL || remote->sin_family != AF_INET)
    {
        return FALSE;
    }

    /* SOCKADDR_IN IPv4 address bytes are stored in network order (127/8 => first byte 0x7F). */
    return ((const UCHAR *)&remote->sin_addr.S_un.S_addr)[0] == 127;
}

static BOOLEAN SvrAlreadyLoopbackProxy(_In_ const SOCKADDR_IN *remote)
{
    UINT32 loopback = 0x0100007F; /* 127.0.0.1 in SOCKADDR_IN layout */
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
    UNREFERENCED_PARAMETER(layerData);
    UNREFERENCED_PARAMETER(flowContext);

    if (filter != NULL && filter->context == SVR_FILTER_RAW_CONTEXT_RUNTIME_CAPTURE)
    {
        SvrCaptureRuntimeAppId(inFixedValues, inMetaValues, filter, classifyOut);
        return;
    }

    InterlockedIncrement(&gClassifyEntries);

    if (classifyOut != NULL)
    {
        gLastRights = (UINT32)classifyOut->rights;
    }

    if (filter != NULL)
    {
        gLastFilterId = filter->filterId;
    }

    if ((classifyOut->rights & FWPS_RIGHT_ACTION_WRITE) == 0)
    {
        InterlockedIncrement(&gExitNoActionWrite);
        return;
    }

    /* Default fail-open: permit unless we successfully rewrite. */
    classifyOut->actionType = FWP_ACTION_PERMIT;

    if (!gEnabled)
    {
        InterlockedIncrement(&gExitDisabled);
        return;
    }

    if (gProxyPid == 0)
    {
        InterlockedIncrement(&gExitProxyPidZero);
        return;
    }

    if (gProxyPort == 0)
    {
        InterlockedIncrement(&gExitProxyPortZero);
        return;
    }

    if (gRedirectHandle == NULL)
    {
        InterlockedIncrement(&gExitRedirectHandleNull);
        return;
    }

    if (classifyContext == NULL)
    {
        InterlockedIncrement(&gExitClassifyContextNull);
        return;
    }

    UINT64 pid = 0;
    if (FWPS_IS_METADATA_FIELD_PRESENT(inMetaValues, FWPS_METADATA_FIELD_PROCESS_ID))
    {
        pid = inMetaValues->processId;
    }

    gLastClassifyPid = pid;

    /* Never redirect the proxy's own sockets (loop prevention). */
    if (pid == 0)
    {
        InterlockedIncrement(&gExitPidZero);
        return;
    }

    if (pid == gProxyPid)
    {
        InterlockedIncrement(&gExitProxyPid);
        return;
    }

    UINT64 classifyHandle = 0;
    NTSTATUS status = FwpsAcquireClassifyHandle((void *)classifyContext, 0, &classifyHandle);
    if (!NT_SUCCESS(status))
    {
        InterlockedIncrement(&gAcquireClassifyHandleFailures);
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
        InterlockedIncrement(&gAcquireWritableLayerDataFailures);
        FwpsReleaseClassifyHandle(classifyHandle);
        classifyOut->actionType = FWP_ACTION_PERMIT;
        return;
    }

    SOCKADDR_IN *remote = (SOCKADDR_IN *)&request->remoteAddressAndPort;
    if (SvrAlreadyLoopbackProxy(remote))
    {
        InterlockedIncrement(&gAlreadyLoopbackProxy);
        SvrPermitWritableConnectWithoutRedirect(classifyHandle, request);
        classifyOut->actionType = FWP_ACTION_PERMIT;
        return;
    }

    if (SvrIsIpv4LoopbackDestination(remote))
    {
        InterlockedIncrement(&gLoopbackDestinationBypass);
        SvrPermitWritableConnectWithoutRedirect(classifyHandle, request);
        classifyOut->actionType = FWP_ACTION_PERMIT;
        return;
    }

    SVR_REDIRECT_CONTEXT *ctx = (SVR_REDIRECT_CONTEXT *)ExAllocatePool2(
        POOL_FLAG_NON_PAGED,
        sizeof(SVR_REDIRECT_CONTEXT),
        SVR_POOL_TAG);
    if (ctx == NULL)
    {
        InterlockedIncrement(&gAllocationFailures);
        SvrApplyModifiedLayerDataTracked(classifyHandle, request, 0, FALSE);
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

    SvrApplyModifiedLayerDataTracked(classifyHandle, request, 0, TRUE);
    FwpsReleaseClassifyHandle(classifyHandle);

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
