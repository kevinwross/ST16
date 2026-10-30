namespace ST16.VM;

/// <summary>Fault codes (spec §6). Code 0 means "no fault".</summary>
public enum FaultCode : byte
{
    None = 0,
    Div0 = 1,
    StackUf = 2,
    StackOf = 3,
    Illop = 4,
    AddrF = 5,
    CallOf = 6,
    CallUf = 7,
}

/// <summary>Canonical mnemonic names for the fault codes (spec §6 table).</summary>
public static class FaultCodeNames
{
    private static readonly string[] Names =
    {
        "NONE", "DIV0", "STACKUF", "STACKOF", "ILLOP", "ADDRF", "CALLOF", "CALLUF",
    };

    public static string Name(FaultCode code) => Names[(int)code];
}
