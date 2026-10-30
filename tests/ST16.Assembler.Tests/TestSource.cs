using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>Shared source fixtures and golden word images (spec §9).</summary>
public static class TestSource
{
    /// <summary>Spec §9.2 — "a loop with a memory counter", copied verbatim
    /// (counter is 9 per the 2026-10-30 erratum so the example prints 10 asterisks).</summary>
    public const string Starloop = """
        ; Print ten asterisks using a counter held in RAM.
        main:
                PUSH 9
                PUSH16 cnt
                STORE             ; mem[cnt] = 9
                PUSH16 0x2A       ; '*'
        loop:
                DUP               ; ... ch ch
                PUSH16 0xF000
                STORE             ; ... ch      (print)
                PUSH16 cnt
                LOAD              ; ... ch m
                DUP               ; ... ch m m
                JZ done           ; ... ch m    (m consumed if taken)
                PUSH 1
                SUB               ; ... ch (m-1)
                PUSH16 cnt
                STORE             ; ... ch      (mem[cnt] = m-1)
                JMP loop
        done:
                DROP              ; (discard '*')
                HALT
        cnt:
                .dw 0
        """;

    /// <summary>
    /// Spec §9.3 — the full 25-word encoding of the starloop program (main at 0x0000).
    /// The <c>JMP loop</c> header is 0x1F00: JMP is opcode 0x1F per the frozen §5.5 table
    /// (0x21 is RET — the two must not share an encoding).
    /// </summary>
    public static readonly ushort[] StarloopWords =
    {
        0x1B09, 0x1C00, 0x0018, 0x1E00, 0x1C00, 0x002A,
        0x0000, 0x1C00, 0xF000, 0x1E00, 0x1C00, 0x0018,
        0x1D00, 0x0000, 0x2207, 0x1B01, 0x0600, 0x1C00,
        0x0018, 0x1E00, 0x1F00, 0x0006, 0x0400, 0x2400,
        0x0000,
    };

    /// <summary>Spec §9.1 — the minimal callee.</summary>
    public const string Sq = "sq:\n    DUP\n    MUL\n    RET\n";

    /// <summary>DUP (0x00), MUL (0x08), RET (0x21).</summary>
    public static readonly ushort[] SqWords = { 0x0000, 0x0800, 0x2100 };

    /// <summary>Fails the assertion with the assembler's diagnostics as the message.</summary>
    public static void AssertSucceeded(AssembleResult r) =>
        Assert.True(r.Success, string.Join("\n", r.Diagnostics.Select(d => d.ToString())));

    /// <summary>The first error diagnostic, asserting there is at least one.</summary>
    public static Diagnostic FirstError(AssembleResult r)
    {
        var d = r.Diagnostics.Where(x => x.Level == "error").ToList();
        Assert.NotEmpty(d);
        return d[0];
    }
}
