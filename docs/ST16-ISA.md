# ST16 Instruction Set Architecture Specification

**Version 1.0** · Status: Draft for review

Amended during v1.0 review: operand and call stack depths raised from 16 to
32 slots (see §2.1 and §10). No encoding or opcode changes.

ST16 is a 16-bit, zero-address (stack) virtual CPU designed to teach computer
architecture. Every value — data, addresses, branch targets — lives on the
operand stack. There are no general-purpose registers and no flags register:
the machine consists of a program counter, a 32-slot operand stack, a hidden
32-slot call stack, and a word-addressed memory that also contains the I/O
ports.

---

## 1. Design goals

| Goal | Consequence |
|---|---|
| Explainable in one sentence per instruction | Zero-addressing; one operand format family |
| No hidden state students can get wrong | No flags register; call stack is hidden but inspectable |
| Real-world concepts without complexity | PC-relative branches, wrap-around overflow, memory-mapped I/O, traps (reserved) |
| Room to grow | 23 of 64 opcode slots are reserved as student extension space |

**Explicit non-goals (v1):** byte addressing, trap vectors / exception
handlers, multi-precision arithmetic, pipelining, interrupts.

---

## 2. Machine model

### 2.1 Datapath

- **Word size:** 16 bits, two's complement. All arithmetic wraps modulo 2¹⁶.
- **Program counter (PC):** 16 bits, word-granular.
- **Operand stack:** 32 slots × 16 bits. Slot **s1 is the top**; pushes place
  the new value in s1, pops remove s1.
- **Call stack:** 32 slots × 16 bits, hidden from the ISA except via
  `CALL`/`RET`. Exists so that stack discipline is visible and debuggable.
- **Stack depth is machine configuration.** The reference ST16 has 32 slots
  in each stack. A conforming implementation may expose both depths as
  configuration (for example, a strict-mode depth of 8 for stack-discipline
  exercises); fault behavior is identical at any depth, and the configured
  depths are part of the machine's definition.
- **Memory:** 65,536 words (128 KiB), see §3.

```
 operand stack (top at top)
 ┌───────┐
 │  s1   │  ← top
 ├───────┤
 │  s2   │
 ├───────┤
 │  s3   │
 │  ⋮    │
 ├───────┤
 │  s32  │  ← bottom
 └───────┘
```

### 2.2 No flags

Comparison instructions **push** `1` or `0`; branch instructions **pop**
their condition from the stack. Nothing else branches on implicit state.

### 2.3 Atomicity

Each instruction is atomic: either all of its effects (stack, memory, PC,
call stack, I/O) occur, or the machine has faulted and **none** of them did.

### 2.4 Timing model

One instruction per cycle. Two-word instructions (`PUSH16`, `JMP`, `CALL`)
also take one cycle — a deliberate modeling simplification so that cycle
counting stays simple for students.

### 2.5 Initial state

On reset, or on loading a program:

- PC = `0x0000`
- operand stack empty, call stack empty
- RAM zero-filled
- instruction counter = 0
- machine state: **RUNNING**

---

## 3. Memory map

Memory is **word-addressed**: address `A` holds the 16-bit word `mem[A]`.
There is no byte addressing in v1.

| Range | Size | Contents | Access |
|---|---|---|---|
| `0x0000` – `0xEFFF` | 61,440 words (120 KiB) | RAM: program + data | read/write |
| `0xF000` – `0xFFFF` | 4,096 ports (8 KiB) | I/O ports | see §7 |

Accessing an address whose port is unmapped (see §7) is an **ADDRF** fault.

---

## 4. Instruction encoding

The instruction stream is a sequence of 16-bit words. Every instruction
begins with a header word:

```
 bit 15        10 | 9         0
 ┌──────────────────┬─────────────┐
 │  opcode (6 bits) │ operand(10) │
 └──────────────────┴─────────────┘
```

- **Opcode:** bits 15–10. 64 possible values; 41 defined, 23 reserved
  (reserved opcodes cause an **ILLOP** fault).
- **Operand field:** bits 9–0. Its meaning depends on the instruction:
  - **ignored** — most instructions. The value is reserved (assemblers emit
    `0`); a conforming implementation must not depend on it.
  - **signed 10-bit immediate** — `PUSH`. Two's complement, range −512…511.
  - **signed 10-bit relative offset** — `JZ`, `JNZ`. Two's complement,
    range −511…+511 words, measured **from the next instruction**:
    `target = (PC + 1) + offset`.
- **Two-word instructions** — `PUSH16`, `JMP`, `CALL`. The header word's
  operand field is reserved (`0`); the following word is an **absolute 16-bit
  value** (literal, or jump/call target). After any two-word instruction the
  PC has advanced by 2.

Branch distance note: `JZ`/`JNZ` reach ±511 words; anything farther (or an
absolute target) uses `JMP`/`CALL`. This short/long distinction is
intentional and mirrors real ISAs.

---

## 5. Instruction reference

Notation: stack effects are written `a b → y` where the rightmost symbol is
the **top** of the stack after execution. `x` denotes a stack value; `i` a
signed immediate; `w` an absolute 16-bit word; `A` an address; `m` a memory
location.

### 5.1 Stack manipulation (0x00–0x04)

| Op | Mnemonic | Stack effect | Description |
|----|----------|--------------|-------------|
| 0x00 | `DUP` | `x → x x` | Push a copy of the top of stack. |
| 0x01 | `SWAP` | `a b → b a` | Exchange the top two elements. |
| 0x02 | `OVER` | `a b → a b a` | Copy the second element onto the top. |
| 0x03 | `ROT` | `a b c → b c a` | Rotate the top three elements. |
| 0x04 | `DROP` | `a →` | Discard the top element. |

### 5.2 Arithmetic (0x05–0x10)

All operate on the top two elements (or one, for `NEG`) and push one result.
Results wrap modulo 2¹⁶; no overflow fault.

| Op | Mnemonic | Stack effect | Description |
|----|----------|--------------|-------------|
| 0x05 | `ADD` | `a b → a+b` | Sum, low 16 bits. |
| 0x06 | `SUB` | `a b → a−b` | Top element is the **subtrahend** (so `x 1 SUB → x−1`). |
| 0x07 | `NEG` | `a → −a` | Negate. `NEG` of `0x8000` yields `0x8000` (wrap). |
| 0x08 | `MUL` | `a b → low16(a×b)` | 32-bit product, low 16 bits pushed. |
| 0x09 | `DIV` | `a b → ⌊a÷b⌋` | Signed division of second by top, quotient **truncated toward zero**. `0x8000 ÷ −1` wraps to `0x8000` (no fault). |
| 0x0A | `MOD` | `a b → a mod b` | Remainder has the sign of the dividend (C-99 style). `x mod x = 0` for any `x ≠ 0`. |
| 0x0B | `AND` | `a b → a ∧ b` | Bitwise AND. |
| 0x0C | `OR` | `a b → a ∨ b` | Bitwise OR. |
| 0x0D | `XOR` | `a b → a ⊕ b` | Bitwise XOR. |
| 0x0E | `NOT` | `a → ~a` | Bitwise complement. |
| 0x0F | `SLL` | `n c → c ≪ (n & 0xF)` | Shift left; **count** is popped first, masked to 0–15. |
| 0x10 | `SLR` | `n c → c ≫ (n & 0xF)` | Arithmetic shift right (sign-extending). |

`DIV`/`MOD` fault with **DIV0** when the divisor `b` (top of stack) is zero.

### 5.3 Comparison (0x11–0x1A)

Each pops the top two elements and pushes `1` if the condition
(second `op` top) holds, else `0`.

| Op | Mnemonic | Condition | Interpretation |
|----|----------|-----------|----------------|
| 0x11 | `EQ` | `a == b` | Pattern equality (identical for signed or unsigned). |
| 0x12 | `NE` | `a != b` | Pattern inequality. |
| 0x13 | `LT` | `a < b` | Signed. |
| 0x14 | `GT` | `a > b` | Signed. |
| 0x15 | `LE` | `a ≤ b` | Signed. |
| 0x16 | `GE` | `a ≥ b` | Signed. |
| 0x17 | `ULT` | `a < b` | Unsigned. |
| 0x18 | `UGT` | `a > b` | Unsigned. |
| 0x19 | `ULE` | `a ≤ b` | Unsigned. |
| 0x1A | `UGE` | `a ≥ b` | Unsigned. |

Signed/unsigned comparison pairs exist because address arithmetic and
unsigned magnitudes compare differently above `0x8000`.

### 5.4 Memory (0x1B–0x1E)

| Op | Mnemonic | Format | Stack effect | Description |
|----|----------|--------|--------------|-------------|
| 0x1B | `PUSH i` | 1 word | `→ i` | Push signed 10-bit immediate. |
| 0x1C | `PUSH16 w` | 2 words | `→ w` | Push absolute 16-bit literal. |
| 0x1D | `LOAD` | 1 word | `A → mem[A]` | Pop address, push its word. **ADDRF** if unmapped. |
| 0x1E | `STORE` | 1 word | `v A →` | Pop address (top), then value; write `mem[A] = v`. **ADDRF** if unmapped. |

Addresses and values are indistinguishable on the stack — the instruction
decides which is which. This is the core idea of a pure zero-address
machine.

### 5.5 Control flow (0x1F–0x24)

| Op | Mnemonic | Format | Description |
|----|----------|--------|-------------|
| 0x1F | `JMP t` | 2 words | Unconditional branch: `PC ← t`. |
| 0x20 | `CALL t` | 2 words | `CALL` pushes `PC + 2` onto the call stack, then `PC ← t`. **CALLOF** if the call stack is full. |
| 0x21 | `RET` | 1 word | `PC ←` top of call stack, which is popped. **CALLUF** if the call stack is empty. |
| 0x22 | `JZ i` | 1 word | Pop `x`; if `x == 0` then `PC ← PC + 1 + i`, else `PC ← PC + 1`. |
| 0x23 | `JNZ i` | 1 word | Pop `x`; if `x != 0` then `PC ← PC + 1 + i`, else `PC ← PC + 1`. |
| 0x24 | `HALT` | 1 word | Stop the machine with termination code 0 (normal completion, not a fault). |

### 5.6 Reserved opcodes (0x25–0x3F)

Any opcode in this range causes an **ILLOP** fault. They are deliberately
reserved as student extension space (e.g., course exercises can propose new
instructions, and a student implementation can extend the decoder).

---

## 6. Faults

On a fault the machine enters the **FAULTED** state and halts. The faulting
instruction has no partial effects (atomicity, §2.3). The machine reports:

- fault code
- PC of the faulting instruction
- last 16 values pushed / top of stack (implementation detail, for debugging)

| Code | Mnemonic | Cause |
|------|----------|-------|
| 1 | `DIV0` | `DIV` or `MOD` with divisor 0. |
| 2 | `STACKUF` | Any instruction needs more stack depth than is available. |
| 3 | `STACKOF` | A `DUP`, `PUSH`, `PUSH16`, `LOAD`, arithmetic, or comparison result would overflow the 32-slot stack. |
| 4 | `ILLOP` | Opcode `0x25`–`0x3F` (reserved/undefined). |
| 5 | `ADDRF` | `LOAD`/`STORE` to a port that is unmapped (see §7). |
| 6 | `CALLOF` | `CALL` with a full call stack. |
| 7 | `CALLUF` | `RET` with an empty call stack. |

`HALT` (termination code 0) is **not** a fault.

> v1 has no trap vectors. A future version may add a handler vector at
> address `0x0000`; nothing in v1 depends on what is stored there.

---

## 7. I/O ports

I/O is memory-mapped: `LOAD`/`STORE` to port addresses performs I/O. There
are no dedicated I/O instructions — the uniformity is intentional
("everything is memory").

| Port | Function | `LOAD` (read) | `STORE` (write) |
|------|----------|----------------|-----------------|
| `0xF000` | Console out | `0` | Output the low 8 bits of `v` as one character (high byte ignored). |
| `0xF001` | Console in | Next input character in the low 8 bits (high byte 0), or `0xFFFF` if no input is available (non-blocking). | Ignored (no-op). |
| `0xF002` | Instruction counter | Current instruction count (low 16 bits). | Ignored (no-op). |
| `0xF003`–`0xFFFF` | Unmapped | **ADDRF** fault. | **ADDRF** fault. |

The instruction counter increments after each completed instruction,
including `HALT`.

---

## 8. Assembly language

ST16 programs are written in a minimal assembly language and assembled to a
flat file of 16-bit words (little-endian byte pairs in the file).

### 8.1 Syntax

```
[ label :] mnemonic [ operand ] ; comment
```

- Mnemonics are case-insensitive; labels are case-sensitive.
- A label is a word followed by `:`, optionally on its own line.
- Comments run from `;` to end of line.
- Directives start with `.`.

### 8.2 Operands and expressions

| Form | Example | Value |
|---|---|---|
| Decimal | `42`, `-7` | Signed/unsigned decimal. |
| Hex | `0x8000` | 16-bit hex. |
| Binary | `0b1010` | 16-bit binary. |
| Label | `loop` | Address of the label. |
| Constant + label | `loop + 4` | Address arithmetic (v1: one label, one constant, `+`/`−` only). |

Operand ranges are checked at assembly time:

- `PUSH`: −512…511.
- `JZ`/`JNZ`: label or integer; assembler computes the **relative offset**
  `target − (instr_addr + 1)` and checks it fits in signed 10 bits.
- `PUSH16`, `JMP`, `CALL`: 0…65535 or a label.

### 8.3 Directives

| Directive | Effect |
|---|---|
| `.org addr` | Set the position counter (default `0x0000`). |
| `.dw expr[, expr…]` | Reserve one word per expression, initialized to its value. |
| `.db b1 [b2]` | Store byte(s) as one word; **first byte is the low 8 bits** (`.db 0x12, 0x34` emits word `0x3412`). |
| `.ds n` | Reserve `n` words, zero-filled. |

### 8.4 Assembler errors

Line-numbered diagnostics for: undefined label, duplicate label, operand out
of range, branch offset out of range, overlap (`.org` backwards or `.dw`
beyond the 120 KiB RAM region), and non-word-aligned end of file.

---

## 9. Examples

### 9.1 A minimal callee (`sq`)

```
; sq(x): return x * x
; Entry: ... x        Exit: ... x*x
; The callee must not disturb the stack below its argument.
sq:
        DUP
        MUL
        RET
```

Call it:

```
        PUSH 7
        CALL sq
        ; stack top is 49
```

### 9.2 A loop with a memory counter

```
; Print ten asterisks using a counter held in RAM.
main:
        PUSH 10
        PUSH16 cnt
        STORE             ; mem[cnt] = 10
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
```

Stack trace at the top of each loop iteration: `[ch, m]` (bottom → top),
where `ch = 0x2A` and `m` counts down from 10.

### 9.3 What the example above compiles to

`main` starts at `0x0000`. Remember: `PUSH16`/`JMP`/`CALL` are two-word
instructions, the `PUSH` operand field is bits 9–0, and `JZ` encodes the
signed offset from the **next** instruction.

| Address | Hex word | Source |
|---|---|---|
| 0x0000 | `0x1B0A` | `PUSH 10` (op `0x1B`, imm `10` = `0x00A`) |
| 0x0001 | `0x1C00` | `PUSH16 cnt` (header) |
| 0x0002 | `0x0018` | `cnt` = `0x0018` |
| 0x0003 | `0x1E00` | `STORE` |
| 0x0004 | `0x1C00` | `PUSH16 0x2A` (header) |
| 0x0005 | `0x002A` | literal |

Full program — 25 words (offset: word):

```
0x0000 0x1B0A   0x0001 0x1C00   0x0002 0x0018   0x0003 0x1E00
0x0004 0x1C00   0x0005 0x002A   0x0006 0x0000   0x0007 0x1C00
0x0008 0xF000   0x0009 0x1E00   0x000A 0x1C00   0x000B 0x0018
0x000C 0x1D00   0x000D 0x0000   0x000E 0x2207   0x000F 0x1B01
0x0010 0x0600   0x0011 0x1C00   0x0012 0x0018   0x0013 0x1E00
0x0014 0x2100   0x0015 0x0006   0x0016 0x0400   0x0017 0x2400
0x0018 0x0000
```

Key points: `PUSH 10` is `0x1B0A` (the immediate lives in bits 9–0); the
`JZ done` at `0x000E` encodes offset `+7` (next instruction `0x000F`,
`done` at `0x0016`); the `JMP loop` at `0x0014` is a two-word absolute
branch to `0x0006`.

---

## 10. Design notes (why it is this way)

- **Pure zero-address.** Keeping the ISA strictly stack-based means there is
  exactly one mental model: the stack. Addresses are just values; teaching
  "load an address, then load the value" *is* teaching indirection.
- **Hidden call stack.** Real CPUs hide their return addresses too. Making
  them inspectable (the simulator must always be able to print the call
  stack) turns "who called what" into a debuggable fact rather than a
  convention students must maintain by hand.
- **32-slot stacks, configurable.** Recursive demos need headroom: `fib(n)`
  consumes `n + 1` call frames, so a 16-deep call stack faults on `fib(15)`.
  32 keeps the fault realistically reachable (`fib(31)`) while giving student
  programs room. Implementations may expose the depths as configuration so
  courses can run "strict mode" (small depths) as stack-discipline exercises.
- **No flags.** A flags register is the classic hidden-state trap in
  teaching. Pushing `0`/`1` makes data dependency explicit in every
  program — and makes a wrong `DROP` immediately visible on the stack view.
- **Short and long branches.** 10-bit relative offsets force students to
  encounter branch distance, a concept every real ISA has, without
  introducing a third instruction format.
- **Memory-mapped I/O.** One addressing model for memory and devices,
  matching the RISC-V / ARM / x86 world; no special `IN`/`OUT` opcodes to
  memorize.
- **Faulting, not trapping, in v1.** The first exposure to exceptions
  should be "what went wrong and where", not "how do you write a handler".
  Trap vectors are the natural v1.1.

---

## 11. Reserved extension space

| Range | Intended future use (non-normative) |
|---|---|
| `0x25`–`0x3F` | New instructions; course exercises may reserve slots here. Any extension must not reuse a v1 opcode or change v1 semantics. |
| `0x0000` (memory) | Trap vector, if exception handlers are added. |
| Ports `0xF003`–`0xFFFF` | Additional devices (e.g., a virtual timer, serial line, framebuffer). |

Any v1.1 change must be published as a new version of this document with a
changelog; the opcode table above is frozen for v1.

---

## 12. Normative summary for implementers

1. 16-bit two's-complement datapath; wrap-around arithmetic, no overflow
   fault.
2. Operand and call stacks of 32 slots each (reference configuration);
   implementations may expose both depths as configuration (see §2.1).
3. 61,440 words of RAM at `0x0000`–`0xEFFF`; I/O ports at `0xF000`–`0xFFFF`
   as in §7.
4. 6-bit opcode + 10-bit operand header; two-word forms for `PUSH16`,
   `JMP`, `CALL` as in §4.
5. Semantics exactly as in §5; fault behavior exactly as in §6.
6. One instruction per cycle; initial state as in §2.5; instruction files
   are little-endian 16-bit words.
7. `JZ`/`JNZ` offset is relative to `PC + 1` (the next instruction).
8. `CALL` return address is `PC + 2` (the word after the two-word `CALL`).
