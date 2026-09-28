namespace SelectiveVpnRouter.Network;

internal static class WfpNativeStatus
{
    public const uint FwpEInvalidWeight = 0x80320025;
    public const uint ErrorInvalidSecurityDescr = 0x0000053A;

    public static string Describe(uint status) =>
        status switch
        {
            0 => "SUCCESS",
            FwpEInvalidWeight => "FWP_E_INVALID_WEIGHT / nedopustimyj ves WFP-filtra",
            ErrorInvalidSecurityDescr => "ERROR_INVALID_SECURITY_DESCR (1338) / uslovie moglo byt interpretirovano kak security descriptor",
            _ => "0x" + status.ToString("X8"),
        };
}