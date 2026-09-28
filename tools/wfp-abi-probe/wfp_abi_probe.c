#include <stdio.h>
#include <stddef.h>
#include <stdint.h>
#include <windows.h>
#include <initguid.h>
#include <fwptypes.h>
#include <fwpmtypes.h>
#include <fwpmu.h>

#define OFF(T, M) (uint32_t)offsetof(T, M)

static void print_guid(const char* name, const GUID* g) {
    printf("%s=%08lx-%04x-%04x-%02x%02x-%02x%02x%02x%02x%02x%02x\n",
        name,
        g->Data1, g->Data2, g->Data3,
        g->Data4[0], g->Data4[1], g->Data4[2], g->Data4[3],
        g->Data4[4], g->Data4[5], g->Data4[6], g->Data4[7]);
}

static void print_fwp_data_types(void) {
    printf("FWP_BYTE_BLOB_TYPE=%u\n", (unsigned)FWP_BYTE_BLOB_TYPE);
    printf("FWP_SECURITY_DESCRIPTOR_TYPE=%u\n", (unsigned)FWP_SECURITY_DESCRIPTOR_TYPE);
    printf("FWP_V4_ADDR_MASK=%u\n", (unsigned)FWP_V4_ADDR_MASK);
    printf("FWP_RANGE_TYPE=%u\n", (unsigned)FWP_RANGE_TYPE);
}

static void print_fwp_value0(void) {
    printf("FWP_VALUE0=%zu\n", sizeof(FWP_VALUE0));
    printf("FWP_VALUE0.type=%u\n", OFF(FWP_VALUE0, type));
}

static void print_fwp_condition_value0(void) {
    printf("FWP_CONDITION_VALUE0=%zu\n", sizeof(FWP_CONDITION_VALUE0));
    printf("FWP_CONDITION_VALUE0.type=%u\n", OFF(FWP_CONDITION_VALUE0, type));
}

static void print_fwp_byte_blob(void) {
    printf("FWP_BYTE_BLOB=%zu\n", sizeof(FWP_BYTE_BLOB));
    printf("FWP_BYTE_BLOB.size=%u\n", OFF(FWP_BYTE_BLOB, size));
    printf("FWP_BYTE_BLOB.data=%u\n", OFF(FWP_BYTE_BLOB, data));
}

static void print_fwpm_action0(void) {
    printf("FWPM_ACTION0=%zu\n", sizeof(FWPM_ACTION0));
    printf("FWPM_ACTION0.type=%u\n", OFF(FWPM_ACTION0, type));
}

static void print_fwpm_filter_condition0(void) {
    printf("FWPM_FILTER_CONDITION0=%zu\n", sizeof(FWPM_FILTER_CONDITION0));
    printf("FWPM_FILTER_CONDITION0.fieldKey=%u\n", OFF(FWPM_FILTER_CONDITION0, fieldKey));
    printf("FWPM_FILTER_CONDITION0.matchType=%u\n", OFF(FWPM_FILTER_CONDITION0, matchType));
    printf("FWPM_FILTER_CONDITION0.conditionValue=%u\n", OFF(FWPM_FILTER_CONDITION0, conditionValue));
}

static void print_fwpm_filter0(void) {
    printf("FWPM_FILTER0=%zu\n", sizeof(FWPM_FILTER0));
    printf("FWPM_FILTER0.filterKey=%u\n", OFF(FWPM_FILTER0, filterKey));
    printf("FWPM_FILTER0.displayData=%u\n", OFF(FWPM_FILTER0, displayData));
    printf("FWPM_FILTER0.flags=%u\n", OFF(FWPM_FILTER0, flags));
    printf("FWPM_FILTER0.providerKey=%u\n", OFF(FWPM_FILTER0, providerKey));
    printf("FWPM_FILTER0.providerData=%u\n", OFF(FWPM_FILTER0, providerData));
    printf("FWPM_FILTER0.layerKey=%u\n", OFF(FWPM_FILTER0, layerKey));
    printf("FWPM_FILTER0.subLayerKey=%u\n", OFF(FWPM_FILTER0, subLayerKey));
    printf("FWPM_FILTER0.weight=%u\n", OFF(FWPM_FILTER0, weight));
    printf("FWPM_FILTER0.numFilterConditions=%u\n", OFF(FWPM_FILTER0, numFilterConditions));
    printf("FWPM_FILTER0.filterCondition=%u\n", OFF(FWPM_FILTER0, filterCondition));
    printf("FWPM_FILTER0.action=%u\n", OFF(FWPM_FILTER0, action));
    printf("FWPM_FILTER0.context=%u\n", OFF(FWPM_FILTER0, rawContext));
    printf("FWPM_FILTER0.reserved=%u\n", OFF(FWPM_FILTER0, reserved));
    printf("FWPM_FILTER0.filterId=%u\n", OFF(FWPM_FILTER0, filterId));
    printf("FWPM_FILTER0.effectiveWeight=%u\n", OFF(FWPM_FILTER0, effectiveWeight));
}

int main(void) {
    print_fwp_data_types();
    print_guid("FWPM_CONDITION_ALE_APP_ID", &FWPM_CONDITION_ALE_APP_ID);
    print_fwp_value0();
    print_fwp_condition_value0();
    print_fwp_byte_blob();
    print_fwpm_action0();
    print_fwpm_filter_condition0();
    print_fwpm_filter0();
    return 0;
}
