#include "SelectiveVpnCallout.h"

UINT32 gProxyPid = 0;
UINT16 gProxyPort = 0;
BOOLEAN gEnabled = FALSE;
UINT32 gCalloutId = 0;
HANDLE gEngineHandle = NULL;
HANDLE gRedirectHandle = NULL;

static WDFDEVICE gDevice = NULL;

static NTSTATUS SvrDeviceControl(
    WDFQUEUE Queue,
    WDFREQUEST Request,
    size_t OutputBufferLength,
    size_t InputBufferLength,
    ULONG IoControlCode
)
{
    UNREFERENCED_PARAMETER(Queue);
    UNREFERENCED_PARAMETER(OutputBufferLength);

    NTSTATUS status = STATUS_SUCCESS;
    if (IoControlCode == SVR_IOCTL_SET_TARGET)
    {
        SVR_TARGET *target = NULL;
        size_t len = 0;
        status = WdfRequestRetrieveInputBuffer(Request, sizeof(SVR_TARGET), (PVOID *)&target, &len);
        if (NT_SUCCESS(status) && target != NULL)
        {
            gProxyPid = target->ProxyPid;
            gProxyPort = target->ProxyPort;
            gEnabled = target->Enabled != 0;
        }
    }
    else
    {
        status = STATUS_INVALID_DEVICE_REQUEST;
    }

    WdfRequestComplete(Request, status);
    return status;
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

    PWDFDEVICE_INIT init = WdfControlDeviceInitAllocate(driver, &SDDL_DEVOBJ_SYS_ALL_ADM_ALL);
    if (init == NULL)
    {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    WdfDeviceInitSetIoType(init, WdfDeviceIoBuffered);
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
    qcfg.EvtIoDeviceControl = SvrDeviceControl;
    WDFQUEUE queue;
    status = WdfIoQueueCreate(gDevice, &qcfg, WDF_NO_OBJECT_ATTRIBUTES, &queue);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    WdfControlFinishInitializing(gDevice);

    status = SvrRegisterCallout();
    return status;
}

VOID SvrEvtDriverUnload(_In_ WDFDRIVER Driver)
{
    UNREFERENCED_PARAMETER(Driver);
    gEnabled = FALSE;
    SvrUnregisterCallout();
}

NTSTATUS SvrRegisterCallout(VOID)
{
    NTSTATUS status = FwpsRedirectHandleCreate(&SVR_PROVIDER_GUID, 0, &gRedirectHandle);
    if (!NT_SUCCESS(status))
    {
        gRedirectHandle = NULL;
    }

    FWPS_CALLOUT callout = { 0 };
    callout.calloutKey = SVR_CALLOUT_CONNECT_REDIRECT_V4;
    callout.classifyFn = SvrClassifyConnectRedirect;
    callout.notifyFn = SvrNotify;
    status = FwpsCalloutRegister(WdfDriverWdmGetDriverObject(WdfGetDriver()), &callout, &gCalloutId);
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
