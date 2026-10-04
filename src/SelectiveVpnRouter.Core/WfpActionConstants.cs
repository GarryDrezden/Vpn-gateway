namespace SelectiveVpnRouter.Core;

public static class WfpActionConstants
{
    public const uint FwpActionFlagTerminating = 0x00001000;
    public const uint FwpActionFlagNonTerminating = 0x00002000;
    public const uint FwpActionFlagCallout = 0x00004000;

    public const uint FwpActionBlock = 0x00000001 | FwpActionFlagTerminating;
    public const uint FwpActionPermit = 0x00000002 | FwpActionFlagTerminating;
    public const uint FwpActionCalloutUnknown = 0x00004005;

    public const uint FwpEInvalidActionType = 0x80320024;
}