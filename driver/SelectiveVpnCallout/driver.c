#include "SelectiveVpnCallout.h"

volatile LONG gOpenHandles = 0;
UINT32 gProxyPid = 0;
UINT16 gProxyPort = 0;
volatile BOOLEAN gEnabled = FALSE;
UINT32 gCalloutId = 0;
HANDLE gRedirectHandle = NULL;
volatile LONG gRedirects = 0;
volatile LONG gRedirectAttempts = 0;
volatile LONG gRedirectApplySuccess = 0;
volatile LONG gRedirectApplyFailures = 0;
volatile LONG gLastRedirectApplyStatus = 0;
volatile LONG gClassifyEntries = 0;
volatile LONG gExitNoActionWrite = 0;
volatile LONG gExitDisabled = 0;
volatile LONG gExitProxyPidZero = 0;
volatile LONG gExitProxyPortZero = 0;
volatile LONG gExitRedirectHandleNull = 0;
volatile LONG gExitClassifyContextNull = 0;
volatile LONG gExitPidZero = 0;
volatile LONG gExitProxyPid = 0;
volatile LONG gAcquireClassifyHandleFailures = 0;
volatile LONG gAcquireWritableLayerDataFailures = 0;
volatile LONG gAlreadyLoopbackProxy = 0;
volatile LONG gLoopbackDestinationBypass = 0;
volatile LONG gAllocationFailures = 0;
volatile UINT64 gLastClassifyPid = 0;
volatile UINT64 gLastFilterId = 0;
volatile UINT32 gLastRights = 0;
volatile LONG gRuntimeCaptureCount = 0;
volatile ULONG gRuntimeAppIdPresent = 0;
volatile ULONG gRuntimeAppIdByteLength = 0;
volatile ULONG gRuntimeAppIdValueType = 0;
volatile UINT64 gRuntimeCapturePid = 0;
volatile UINT64 gRuntimeCaptureFilterId = 0;
volatile ULONG gRuntimeCaptureRights = 0;
WCHAR gRuntimeAppIdText[SVR_RUNTIME_APP_ID_TEXT_CHARS];
KSPIN_LOCK gRuntimeCaptureLock;

static WDFDEVICE gDevice = NULL;

VOID SvrResetRuntimeCapture(VOID)
{
    KIRQL oldIrql;
    KeAcquireSpinLock(&gRuntimeCaptureLock, &oldIrql);
    gRuntimeCaptureCount = 0;
    gRuntimeAppIdPresent = 0;
    gRuntimeAppIdByteLength = 0;
    gRuntimeAppIdValueType = 0;
    gRuntimeCapturePid = 0;
    gRuntimeCaptureFilterId = 0;
    gRuntimeCaptureRights = 0;
    RtlZeroMemory(gRuntimeAppIdText, sizeof(gRuntimeAppIdText));
    KeReleaseSpinLock(&gRuntimeCaptureLock, oldIrql);
}

VOID SvrFailOpen(VOID)
{
    gEnabled = FALSE;
    gProxyPid = 0;
    gProxyPort = 0;
}

VOID SvrEvtFileCreate(
    _In_ WDFDEVICE Device,
    _In_ WDFREQUEST Request,
    _In_ WDFFILEOBJECT FileObject
)
{
    UNREFERENCED_PARAMETER(Device);
    UNREFERENCED_PARAMETER(FileObject);
    InterlockedIncrement(&gOpenHandles);
    WdfRequestComplete(Request, STATUS_SUCCESS);
}

VOID SvrEvtFileCleanup(
    _In_ WDFFILEOBJECT FileObject
)
{
    UNREFERENCED_PARAMETER(FileObject);
    LONG left = InterlockedDecrement(&gOpenHandles);
    if (left <= 0)
    {
        // Service/process died or closed the device: stop redirecting. Fail-open.
        SvrFailOpen();
        gOpenHandles = 0;
    }
}

VOID SvrEvtIoDeviceControl(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t OutputBufferLength,
    _In_ size_t InputBufferLength,
    _In_ ULONG IoControlCode
)
{
    UNREFERENCED_PARAMETER(Queue);
    NTSTATUS status = STATUS_SUCCESS;
    size_t written = 0;

    if (IoControlCode == SVR_IOCTL_SET_TARGET)
    {
        SVR_TARGET *target = NULL;
        size_t len = 0;
        status = WdfRequestRetrieveInputBuffer(Request, sizeof(SVR_TARGET), (PVOID *)&target, &len);
        if (NT_SUCCESS(status) && target != NULL)
        {
            gProxyPid = target->ProxyPid;
            gProxyPort = target->ProxyPort;
            gEnabled = (target->Enabled != 0) && (target->ProxyPid != 0) && (target->ProxyPort != 0);
        }
    }
    else if (IoControlCode == SVR_IOCTL_RESET_RUNTIME_CAPTURE)
    {
        SvrResetRuntimeCapture();
    }
    else if (IoControlCode == SVR_IOCTL_GET_STATUS)
    {
        SVR_STATUS *out = NULL;
        size_t len = 0;
        UNREFERENCED_PARAMETER(InputBufferLength);
        if (OutputBufferLength < sizeof(SVR_STATUS))
        {
            status = STATUS_BUFFER_TOO_SMALL;
        }
        else
        {
            status = WdfRequestRetrieveOutputBuffer(Request, sizeof(SVR_STATUS), (PVOID *)&out, &len);
            if (NT_SUCCESS(status) && out != NULL)
            {
                KIRQL oldIrql;
                KeAcquireSpinLock(&gRuntimeCaptureLock, &oldIrql);

                out->ProxyPid = gProxyPid;
                out->ProxyPort = gProxyPort;
                out->Enabled = gEnabled ? 1 : 0;
                out->CalloutId = gCalloutId;
                out->OpenHandles = (UINT32)gOpenHandles;
                out->Redirects = (UINT32)gRedirectApplySuccess;
                out->RedirectAttempts = (UINT32)gRedirectAttempts;
                out->RedirectApplySuccess = (UINT32)gRedirectApplySuccess;
                out->RedirectApplyFailures = (UINT32)gRedirectApplyFailures;
                out->LastRedirectApplyStatus = (INT32)gLastRedirectApplyStatus;
                out->ClassifyEntries = (UINT32)gClassifyEntries;
                out->ExitNoActionWrite = (UINT32)gExitNoActionWrite;
                out->ExitDisabled = (UINT32)gExitDisabled;
                out->ExitProxyPidZero = (UINT32)gExitProxyPidZero;
                out->ExitProxyPortZero = (UINT32)gExitProxyPortZero;
                out->ExitRedirectHandleNull = (UINT32)gExitRedirectHandleNull;
                out->ExitClassifyContextNull = (UINT32)gExitClassifyContextNull;
                out->ExitPidZero = (UINT32)gExitPidZero;
                out->ExitProxyPid = (UINT32)gExitProxyPid;
                out->AcquireClassifyHandleFailures = (UINT32)gAcquireClassifyHandleFailures;
                out->AcquireWritableLayerDataFailures = (UINT32)gAcquireWritableLayerDataFailures;
                out->AlreadyLoopbackProxy = (UINT32)gAlreadyLoopbackProxy;
                out->AllocationFailures = (UINT32)gAllocationFailures;
                out->LastClassifyPid = gLastClassifyPid;
                out->LastFilterId = gLastFilterId;
                out->LastRights = gLastRights;
                out->StatusPad = SVR_STATUS_VERSION;
                out->RuntimeCaptureCount = (UINT32)gRuntimeCaptureCount;
                out->RuntimeAppIdPresent = gRuntimeAppIdPresent;
                out->RuntimeAppIdByteLength = gRuntimeAppIdByteLength;
                out->RuntimeAppIdValueType = gRuntimeAppIdValueType;
                out->RuntimeProcessId = gRuntimeCapturePid;
                out->RuntimeFilterId = gRuntimeCaptureFilterId;
                out->RuntimeRights = gRuntimeCaptureRights;
                out->RuntimeCapturePad = (UINT32)gLoopbackDestinationBypass;
                RtlCopyMemory(
                    out->RuntimeAppIdText,
                    gRuntimeAppIdText,
                    sizeof(out->RuntimeAppIdText));

                KeReleaseSpinLock(&gRuntimeCaptureLock, oldIrql);
                written = sizeof(SVR_STATUS);
            }
        }
    }
    else
    {
        status = STATUS_INVALID_DEVICE_REQUEST;
    }

    WdfRequestCompleteWithInformation(Request, status, written);
}

NTSTATUS DriverEntry(_In_ PDRIVER_OBJECT DriverObject, _In_ PUNICODE_STRING RegistryPath)
{
    WDF_DRIVER_CONFIG config;
    WDF_DRIVER_CONFIG_INIT(&config, WDF_NO_EVENT_CALLBACK);
    config.EvtDriverUnload = SvrEvtDriverUnload;
    config.DriverInitFlags |= WdfDriverInitNonPnpDriver;

    WDFDRIVER driver;
    NTSTATUS status = WdfDriverCreate(DriverObject, RegistryPath, WDF_NO_OBJECT_ATTRIBUTES, &config, &driver);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    KeInitializeSpinLock(&gRuntimeCaptureLock);
    SvrResetRuntimeCapture();

    PWDFDEVICE_INIT init = WdfControlDeviceInitAllocate(driver, &SDDL_DEVOBJ_SYS_ALL_ADM_ALL);
    if (init == NULL)
    {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    WdfDeviceInitSetIoType(init, WdfDeviceIoBuffered);
    WdfDeviceInitSetExclusive(init, FALSE);

    WDF_FILEOBJECT_CONFIG fileConfig;
    WDF_FILEOBJECT_CONFIG_INIT(&fileConfig, SvrEvtFileCreate, WDF_NO_EVENT_CALLBACK, SvrEvtFileCleanup);
    WdfDeviceInitSetFileObjectConfig(init, &fileConfig, WDF_NO_OBJECT_ATTRIBUTES);

    DECLARE_CONST_UNICODE_STRING(deviceName, SVR_DEVICE_NAME);
    status = WdfDeviceInitAssignName(init, &deviceName);
    if (!NT_SUCCESS(status))
    {
        WdfDeviceInitFree(init);
        return status;
    }

    WDF_OBJECT_ATTRIBUTES attrs;
    WDF_OBJECT_ATTRIBUTES_INIT(&attrs);
    status = WdfDeviceCreate(&init, &attrs, &gDevice);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    DECLARE_CONST_UNICODE_STRING(symlink, SVR_SYMLINK_NAME);
    status = WdfDeviceCreateSymbolicLink(gDevice, &symlink);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    WDF_IO_QUEUE_CONFIG qcfg;
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&qcfg, WdfIoQueueDispatchSequential);
    qcfg.EvtIoDeviceControl = SvrEvtIoDeviceControl;
    WDFQUEUE queue;
    status = WdfIoQueueCreate(gDevice, &qcfg, WDF_NO_OBJECT_ATTRIBUTES, &queue);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    WdfControlFinishInitializing(gDevice);

    status = SvrRegisterCallout(gDevice);
    return status;
}

VOID SvrEvtDriverUnload(_In_ WDFDRIVER Driver)
{
    UNREFERENCED_PARAMETER(Driver);
    SvrFailOpen();
    SvrUnregisterCallout();
}

NTSTATUS SvrRegisterCallout(_In_ WDFDEVICE Device)
{
    UNREFERENCED_PARAMETER(Device);

    NTSTATUS status = FwpsRedirectHandleCreate(&SVR_PROVIDER_GUID, 0, &gRedirectHandle);
    if (!NT_SUCCESS(status))
    {
        gRedirectHandle = NULL;
        return status;
    }

    FWPS_CALLOUT callout = { 0 };
    callout.calloutKey = SVR_CALLOUT_CONNECT_REDIRECT_V4;
    callout.classifyFn = SvrClassifyConnectRedirect;
    callout.notifyFn = SvrNotify;
    callout.flowDeleteFn = NULL;
    status = FwpsCalloutRegister(WdfDeviceWdmGetDeviceObject(gDevice), &callout, &gCalloutId);
    if (!NT_SUCCESS(status))
    {
        FwpsRedirectHandleDestroy(gRedirectHandle);
        gRedirectHandle = NULL;
    }

    return status;
}

VOID SvrUnregisterCallout(VOID)
{
    if (gCalloutId != 0)
    {
        FwpsCalloutUnregisterById(gCalloutId);
        gCalloutId = 0;
    }

    if (gRedirectHandle != NULL)
    {
        FwpsRedirectHandleDestroy(gRedirectHandle);
        gRedirectHandle = NULL;
    }
}
