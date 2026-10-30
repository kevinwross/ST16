namespace ST16.Assembler.Tests;

/// <summary>
/// Golden program sources (spec §9) and additional round-trip programs, plus
/// the documented golden word images.
/// </summary>
internal static class Programs
{
    /// <summary>Spec §9.2, verbatim (assembled with file name "starloop.st16").</summary>
    public const string Starloop =
        """
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

    /// <summary>Spec §9.1 (the calling program plus the callee).</summary>
    public const string Sq =
        """
                PUSH 7
                CALL sq
        sq:
                DUP
                MUL
                RET
        """;

    /// <summary>The §9.2 golden image: 25 words, 0x0000–0x0018.</summary>
    public static readonly ushort[] StarloopWords =
        [
            0x1B09, 0x1C00, 0x0018, 0x1E00, 0x1C00, 0x002A, 0x0000, 0x1C00, 0xF000,
            0x1E00, 0x1C00, 0x0018, 0x1D00, 0x0000, 0x2207, 0x1B01, 0x0600, 0x1C00,
            0x0018, 0x1E00, 0x1F00, 0x0006, 0x0400, 0x2400, 0x0000,
        ];

    /// <summary>The §9.1 golden image: 6 words, 0x0000–0x0005.</summary>
    public static readonly ushort[] SqWords =
        [0x1B07, 0x2000, 0x0003, 0x0000, 0x0800, 0x2100];

    /// <summary>
    /// A program that starts at .org &gt; 0 and exercises .dw, .db, .ds,
    /// both branch forms and most opcodes (round-trip + section test).
    /// Layout: code 0x0010–0x0034, skip/HALT 0x0035, .dw 0x0036, .db 0x0037, .ds 0x0038–0x0039.
    /// </summary>
    public const string OrgGap =
        """
        .org 0x0010
        start:
                PUSH 7
                PUSH -3
                ADD
                SUB
                PUSH16 0x001A
                CALL start
                JZ skip
                JNZ start
                SWAP
                OVER
                ROT
                DROP
                NEG
                MUL
                DIV
                MOD
                AND
                OR
                XOR
                NOT
                SLL
                SLR
                EQ
                NE
                LT
                GT
                LE
                GE
                ULT
                UGT
                ULE
                UGE
                LOAD
                STORE
                RET
        skip:
                HALT
                .dw 0x001A
                .db 0x12, 0x34
                .ds 2
        """;

    /// <summary>
    /// Every one-word opcode (34 of the 37), starting with a non-zero word so the
    /// disassembler's start detection is unambiguous. Not meant to be executed.
    /// </summary>
    public const string AllOneWordOpcodes =
        """
                PUSH 1
                DUP
                SWAP
                OVER
                ROT
                DROP
                ADD
                SUB
                NEG
                MUL
                DIV
                MOD
                AND
                OR
                XOR
                NOT
                SLL
                SLR
                EQ
                NE
                LT
                GT
                LE
                GE
                ULT
                UGT
                ULE
                UGE
                LOAD
                STORE
                RET
        end:
                JZ end
                JNZ end
                HALT
        """;
}
