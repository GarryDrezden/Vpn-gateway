#include "SelectiveVpnCallout.h"

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

    UINT64 pid = 0;
    if (FWPS_IS_METADATA_FIELD_PRESENT(inMetaValues, FWPS_METADATA_FIELD_PROCESS_ID))
    {
        pid = inMetaValues->processId;
    }

    if (!gEnabled || gProxyPid == 0 || gProxyPort == 0 || pid == 0 || pid == gProxyPid)
    {
        classifyOut->actionType = FWP_ACTION_PERMIT;
        return;
    }

    if (classifyContext == NULL || gRedirectHandle == NULL)
    {
        classifyOut->actionType = FWP_ACTION_PERMIT;
        return;
    }

    UINT64 classifyHandle = 0;
    NTSTATUS status = FwpsAcquireClassifyHandle((void *)classifyContext, 0, &classifyHandle);
    if (!NT_SUCCESS(status))
    {
        classifyOut->actionType = FWP_ACTION_PERMIT;
        return;
    }

    FWPS_CONNECT_REQUEST *request = NULL;
    status = FwpsAcquireWritableLayerDataPointer(classifyHandle, filter->filterId, 0, (PVOID *)&request, classifyOut);
    if (!NT_SUCCESS(status) || request == NULL)
    {
        FwpsReleaseClassifyHandle(classifyHandle);
        classifyOut->actionType = FWP_ACTION_PERMIT;
        return;
    }

    SOCKADDR_IN *remote = (SOCKADDR_IN *)&request->remoteAddressAndPort;
    SVR_REDIRECT_CONTEXT ctx = { 0 };
    ctx.RemoteAddr = remote->sin_addr.S_un.S_addr;
    ctx.RemotePort = remote->sin_port;

    IN_ADDR loopback;
    loopback.S_un.S_addr = 0x0100007F; // 127.0.0.1
    remote->sin_family = AF_INET;
    remote->sin_addr = loopback;
    remote->sin_port = RtlUshortByteSwap(gProxyPort);

    request->localRedirectTargetPID = gProxyPid;
    request->localRedirectHandle = gRedirectHandle;
    request->localRedirectContext = &ctx;
    request->localRedirectContextSize = sizeof(ctx);

    FwpsApplyModifiedLayerData(classifyHandle, request, 0);
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
