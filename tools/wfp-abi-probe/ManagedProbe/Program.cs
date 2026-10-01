using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit, Size = 8)]
struct FWP_VALUE0_UNION { [FieldOffset(0)] public IntPtr byteBlob; }

[StructLayout(LayoutKind.Sequential)]
struct FWP_VALUE0_SEQ { public uint type; public FWP_VALUE0_UNION Value; }

[StructLayout(LayoutKind.Explicit, Size = 16)]
struct FWP_VALUE0_EXP { [FieldOffset(0)] public uint type; [FieldOffset(4)] public IntPtr byteBlob; }

[StructLayout(LayoutKind.Explicit, Size = 40)]
struct FWPM_FILTER_CONDITION0_EXP {
    [FieldOffset(0)] public Guid fieldKey;
    [FieldOffset(16)] public uint matchType;
    [FieldOffset(24)] public FWP_VALUE0_EXP conditionValue;
}

[StructLayout(LayoutKind.Sequential)]
struct FWPM_FILTER_CONDITION0_SEQ {
    public Guid fieldKey; public uint matchType; public FWP_VALUE0_EXP conditionValue;
}

class P {
    static void Main() {
        Console.WriteLine($"SEQ FWP_VALUE0={Marshal.SizeOf<FWP_VALUE0_SEQ>()}");
        Console.WriteLine($"EXP FWP_VALUE0={Marshal.SizeOf<FWP_VALUE0_EXP>()}");
        Console.WriteLine($"SEQ cond={Marshal.SizeOf<FWPM_FILTER_CONDITION0_SEQ>()} cv={Marshal.OffsetOf<FWPM_FILTER_CONDITION0_SEQ>("conditionValue")}");
        Console.WriteLine($"EXP cond={Marshal.SizeOf<FWPM_FILTER_CONDITION0_EXP>()}");
    }
}
