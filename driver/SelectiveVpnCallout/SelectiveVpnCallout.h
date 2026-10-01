#pragma once

#include <ntddk.h>
#include <wdf.h>
#include <wdmsec.h>
#include <fwpsk.h>
#include <fwpmk.h>
#include <ws2ipdef.h>
#include <inaddr.h>
#include <initguid.h>

// Must match SelectiveVpnRouter.Network.WfpSession / CalloutDriverClient.
DEFINE_GUID(SVR_CALLOUT_CONNECT_REDIRECT_V4,
    0x6b3d1f8a, 0x7c2e, 0x4b91, 0x9e, 0x44, 0xa1, 0xf0, 0xc3, 0xd5, 0xe6, 0x09);

DEFINE_GUID(SVR_PROVIDER_GUID,
    0x6b3d1f8a, 0x7c2e, 0x4b91, 0x9e, 0x44, 0xa1, 0xf0, 0xc3, 0xd5, 0xe6, 0x07);

#define SVR_DEVICE_NAME L"\\Device\\SelectiveVpnCallout"
#define SVR_SYMLINK_NAME L"\\DosDevices\\SelectiveVpnCallout"

#define SVR_IOCTL_SET_TARGET CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define SVR_IOCTL_GET_STATUS CTL_CODE(FILE_DEVICE_UNKNOWN, 0x802, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define SVR_IOCTL_RESET_RUNTIME_CAPTURE CTL_CODE(FILE_DEVICE_UNKNOWN, 0x803, METHOD_BUFFERED, FILE_ANY_ACCESS)

#define SVR_STATUS_VERSION 2
#define SVR_FILTER_RAW_CONTEXT_RUNTIME_CAPTURE 0x535652444931ULL /* 'SVRDI1' */
#define SVR_RUNTIME_APP_ID_TEXT_CHARS 512

#define SVR_POOL_TAG 'rvS'

typedef struct _SVR_TARGET {
    UINT32 ProxyPid;
    UINT16 ProxyPort; /* host order */
    UINT16 Enabled;
} SVR_TARGET;

typedef struct _SVR_STATUS {
    UINT32 ProxyPid;
    UINT16 ProxyPort;
    UINT16 Enabled;
    UINT32 CalloutId;
    UINT32 OpenHandles;
    UINT32 Redirects; /* legacy: same as RedirectApplySuccess */
    UINT32 RedirectAttempts;
    UINT32 RedirectApplySuccess;
    UINT32 RedirectApplyFailures;
    INT32 LastRedirectApplyStatus;
    UINT32 ClassifyEntries;
    UINT32 ExitNoActionWrite;
    UINT32 ExitDisabled;
    UINT32 ExitProxyPidZero;
    UINT32 ExitProxyPortZero;
    UINT32 ExitRedirectHandleNull;
    UINT32 ExitClassifyContextNull;
    UINT32 ExitPidZero;
    UINT32 ExitProxyPid;
    UINT32 AcquireClassifyHandleFailures;
    UINT32 AcquireWritableLayerDataFailures;
    UINT32 AlreadyLoopbackProxy;
    UINT32 AllocationFailures;
    UINT64 LastClassifyPid;
    UINT64 LastFilterId;
    UINT32 LastRights;
    UINT32 StatusPad; /* SVR_STATUS_VERSION when extended status is supported */
    UINT32 RuntimeCaptureCount;
    UINT32 RuntimeAppIdPresent;
    UINT32 RuntimeAppIdByteLength;
    UINT32 RuntimeAppIdValueType;
    UINT64 RuntimeProcessId;
    UINT64 RuntimeFilterId;
    UINT32 RuntimeRights;
    UINT32 RuntimeCapturePad;
    WCHAR RuntimeAppIdText[SVR_RUNTIME_APP_ID_TEXT_CHARS];
} SVR_STATUS;

typedef struct _SVR_REDIRECT_CONTEXT {
    UINT32 RemoteAddr; /* network order IPv4 */
    UINT16 RemotePort; /* network order */
    UINT16 Reserved;
} SVR_REDIRECT_CONTEXT;

extern volatile LONG gOpenHandles;
extern UINT32 gProxyPid;
extern UINT16 gProxyPort;
extern volatile BOOLEAN gEnabled;
extern UINT32 gCalloutId;
extern HANDLE gRedirectHandle;
extern volatile LONG gRedirects;
extern volatile LONG gRedirectAttempts;
extern volatile LONG gRedirectApplySuccess;
extern volatile LONG gRedirectApplyFailures;
extern volatile LONG gLastRedirectApplyStatus;
extern volatile LONG gClassifyEntries;
extern volatile LONG gExitNoActionWrite;
extern volatile LONG gExitDisabled;
extern volatile LONG gExitProxyPidZero;
extern volatile LONG gExitProxyPortZero;
extern volatile LONG gExitRedirectHandleNull;
extern volatile LONG gExitClassifyContextNull;
extern volatile LONG gExitPidZero;
extern volatile LONG gExitProxyPid;
extern volatile LONG gAcquireClassifyHandleFailures;
extern volatile LONG gAcquireWritableLayerDataFailures;
extern volatile LONG gAlreadyLoopbackProxy;
extern volatile LONG gAllocationFailures;
extern volatile UINT64 gLastClassifyPid;
extern volatile UINT64 gLastFilterId;
extern volatile UINT32 gLastRights;
extern volatile LONG gRuntimeCaptureCount;
extern volatile ULONG gRuntimeAppIdPresent;
extern volatile ULONG gRuntimeAppIdByteLength;
extern volatile ULONG gRuntimeAppIdValueType;
extern volatile UINT64 gRuntimeCapturePid;
extern volatile UINT64 gRuntimeCaptureFilterId;
extern volatile ULONG gRuntimeCaptureRights;
extern WCHAR gRuntimeAppIdText[SVR_RUNTIME_APP_ID_TEXT_CHARS];
extern KSPIN_LOCK gRuntimeCaptureLock;

VOID SvrResetRuntimeCapture(VOID);

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_UNLOAD SvrEvtDriverUnload;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL SvrEvtIoDeviceControl;
EVT_WDF_DEVICE_FILE_CREATE SvrEvtFileCreate;
EVT_WDF_FILE_CLEANUP SvrEvtFileCleanup;

NTSTATUS SvrRegisterCallout(_In_ WDFDEVICE Device);
VOID SvrUnregisterCallout(VOID);
VOID SvrFailOpen(VOID);

VOID NTAPI SvrClassifyConnectRedirect(
    _In_ const FWPS_INCOMING_VALUES *inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES *inMetaValues,
    _Inout_opt_ VOID *layerData,
    _In_opt_ const VOID *classifyContext,
    _In_ const FWPS_FILTER *filter,
    _In_ UINT64 flowContext,
    _Inout_ FWPS_CLASSIFY_OUT *classifyOut
);

NTSTATUS NTAPI SvrNotify(
    _In_ FWPS_CALLOUT_NOTIFY_TYPE notifyType,
    _In_ const GUID *filterKey,
    _Inout_ FWPS_FILTER *filter
);
