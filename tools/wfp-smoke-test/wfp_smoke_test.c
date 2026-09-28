#include <stdio.h>
#include <stdlib.h>
#include <windows.h>
#include <fwpmtypes.h>
#include <fwpmu.h>

#pragma comment(lib, "fwpuclnt.lib")

static void fail(const char* step, DWORD st) {
    fprintf(stderr, "wfp-smoke-test FAIL at %s: 0x%08lX\n", step, st);
    exit(1);
}

int main(void) {
    HANDLE engine = NULL;
    FWPM_SESSION0 session = {0};
    session.flags = FWPM_SESSION_FLAG_DYNAMIC;

    DWORD st = FwpmEngineOpen0(NULL, RPC_C_AUTHN_WINNT, NULL, &session, &engine);
    if (st != ERROR_SUCCESS) fail("FwpmEngineOpen0", st);

    GUID providerKey;
    CoCreateGuid(&providerKey);
    FWPM_PROVIDER0 provider = {0};
    provider.providerKey = providerKey;
    provider.displayData.name = L"SVR WFP smoke provider";
    provider.displayData.description = L"Temporary ABI smoke test";
    st = FwpmProviderAdd0(engine, &provider, NULL);
    if (st != ERROR_SUCCESS && st != FWP_E_ALREADY_EXISTS) fail("FwpmProviderAdd0", st);

    GUID subLayerKey;
    CoCreateGuid(&subLayerKey);
    FWPM_SUBLAYER0 sub = {0};
    sub.subLayerKey = subLayerKey;
    sub.displayData.name = L"SVR WFP smoke sublayer";
    sub.displayData.description = L"Temporary";
    sub.providerKey = &providerKey;
    sub.weight = 0x7FFF;
    st = FwpmSubLayerAdd0(engine, &sub, NULL);
    if (st != ERROR_SUCCESS && st != FWP_E_ALREADY_EXISTS) fail("FwpmSubLayerAdd0", st);

    WCHAR exePath[MAX_PATH];
    GetSystemDirectoryW(exePath, MAX_PATH);
    wcscat_s(exePath, MAX_PATH, L"\\notepad.exe");

    FWP_BYTE_BLOB* appId = NULL;
    st = FwpmGetAppIdFromFileName0(exePath, &appId);
    if (st != ERROR_SUCCESS || appId == NULL) fail("FwpmGetAppIdFromFileName0", st);

    FWPM_FILTER_CONDITION0 cond = {0};
    cond.fieldKey = FWPM_CONDITION_ALE_APP_ID;
    cond.matchType = FWP_MATCH_EQUAL;
    cond.conditionValue.type = FWP_BYTE_BLOB_TYPE;
    cond.conditionValue.byteBlob = appId;

    GUID filterKey;
    CoCreateGuid(&filterKey);
    FWPM_FILTER0 filter = {0};
    filter.filterKey = filterKey;
    filter.displayData.name = L"SVR smoke filter";
    filter.displayData.description = L"Harmless ABI smoke test";
    filter.providerKey = &providerKey;
    filter.layerKey = FWPM_LAYER_ALE_AUTH_CONNECT_V6;
    filter.subLayerKey = subLayerKey;
    filter.weight.type = FWP_EMPTY;
    filter.numFilterConditions = 1;
    filter.filterCondition = &cond;
    filter.action.type = FWP_ACTION_BLOCK;

    UINT64 filterId = 0;
    st = FwpmFilterAdd0(engine, &filter, NULL, &filterId);
    FwpmFreeMemory0((void**)&appId);
    if (st != ERROR_SUCCESS || filterId == 0) fail("FwpmFilterAdd0", st);

    st = FwpmFilterDeleteByKey0(engine, &filterKey);
    if (st != ERROR_SUCCESS) fail("FwpmFilterDeleteByKey0", st);

    st = FwpmEngineClose0(engine);
    if (st != ERROR_SUCCESS) fail("FwpmEngineClose0", st);

    printf("wfp-smoke-test PASS filterId=%llu\n", (unsigned long long)filterId);
    return 0;
}