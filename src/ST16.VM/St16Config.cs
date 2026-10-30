namespace ST16.VM;

/// <summary>
/// Machine configuration (spec §2.1): the configured stack depths are part of
/// the machine's definition. Defaults match the reference ST16 (32/32);
/// depths as small as 4 power strict-mode stack-discipline exercises.
/// </summary>
public sealed class St16Config
{
    public const ushort MinDepth = 4;
    public const ushort MaxDepth = 256;

    public ushort OperandDepth { get; }
    public ushort CallDepth { get; }

    public St16Config(ushort operandDepth = 32, ushort callDepth = 32)
    {
        if (operandDepth is < MinDepth or > MaxDepth)
            throw new ArgumentOutOfRangeException(
                nameof(operandDepth), operandDepth, $"Operand stack depth must be between {MinDepth} and {MaxDepth}.");
        if (callDepth is < MinDepth or > MaxDepth)
            throw new ArgumentOutOfRangeException(
                nameof(callDepth), callDepth, $"Call stack depth must be between {MinDepth} and {MaxDepth}.");
        OperandDepth = operandDepth;
        CallDepth = callDepth;
    }
}
