# ST16 Development Plan

ST16 is a 16-bit, zero-address (stack) virtual CPU built to teach computer
architecture. This plan defines the build sequence, what each phase delivers,
and when a phase is done.

**Shorthand:** "VM" = the ST16 Virtual Machine (the simulation engine).

## Phase overview

| Phase | Deliverable | Project | Status |
|---|---|---|---|
| 1 | ISA specification v1.0 | `docs/` | ✅ complete (draft, under review) |
| 2 | Virtual Machine with debugging hooks + CLI | `ST16.VM`, `ST16.App` | pending |
| 3 | Assembler, disassembler, `.st16d` debug symbols | `ST16.Assembler` | pending |
| 3.5 | C compiler (core C subset) | `ST16.CC` | pending |
| 4 | Sample program library | `samples/` | pending |
| 5 | WPF debug GUI (combined source + disassembly) | `ST16.Gui` | pending — **must have** |
| 6 (someday) | Verilog reference implementation | `rtl/` | not scheduled |

All phases build against the frozen spec in
[docs/ST16-ISA.md](docs/ST16-ISA.md); any spec change requires a new spec
version and a changelog entry here.

---

## Phase 1 — ISA Specification

**Status: complete (v1.0 draft).**

- [docs/ST16-ISA.md](docs/ST16-ISA.md) — normative v1.0 specification:
  machine model, memory map, encoding, all 37 instructions, fault model,
  I/O ports, assembly syntax, design notes, and the implementer summary (§12).
- The spec's §12 normative summary is the acceptance baseline for every later
  phase.
- Review amendment: operand and call stack depths raised from 16 to 32 slots
  (spec §2.1, §10); the VM exposes both depths as configuration (default 32).

**Definition of done:** spec reviewed and approved; opcode table frozen.

---

## Phase 2 — Virtual Machine (ST16.VM)

The executable core of ST16: a deterministic, testable, **debug-hook-first**
execution engine. The hooks are designed now so the phase-5 GUI is a consumer
of a stable API, not an afterthought.

**Deliverables**

- `src/ST16.VM` — class library, zero UI dependencies.
  - **Core engine** — fetch/decode/execute implementing spec §5–§12 exactly:
    16-bit two's-complement datapath, 32-slot operand stack, 32-slot call
    stack (both depths configurable, default 32 — small "strict-mode" depths
    power stack-discipline exercises), 61,440 words of RAM, I/O ports, one
    instruction per cycle.
  - **Machine states** — `Running`, `Paused` (step or breakpoint),
    `Faulted`, `Halted`; atomic fault semantics (faulting instruction has no
    partial effects).
  - **Debug hooks** (the public surface the GUI will use):
    - *Execution events:* `InstructionCompleted` (index, PC, decoded
      instruction), `FaultOccurred` (code, PC), `Halted` (termination code),
      `BreakpointHit` (address).
    - *Breakpoints:* set/clear by address (multiple); v1 faults and HALT
      are always terminal (break-on-fault / break-on-HALT reserved for
      v1.1 trap vectors).
    - *Watchpoints:* set/clear by address (multiple); `WatchpointHit`
      event (write-only — fires on `STORE` to a watched address).
    - *Stepping:* `StepInto`, `StepOver` (skips into a `CALL` frame and stops
      at the return), `StepOut`.
    - *Run control:* `Run`, `RunUntilBreakpoint`, instruction-limit watchdog
      (bounds runaway loops), `Reset`.
    - *State inspection:* PC, machine state, instruction counter, full
      operand stack (all 32 slots), call stack (return addresses + depth),
      arbitrary memory windows, port register values.
    - *Snapshot/restore:* capture and restore complete machine state
      (powers the GUI's undo and "what-if" stepping).
    - *I/O abstraction:* console in/out ports are served through small
      interfaces so the CLI, the GUI, and tests can each supply their own
      input source and capture their own output. Fully deterministic: a VM is
      a pure function of (program, input sequence).
- `src/ST16.App` — the single `st16` executable: all functionality (VM,
  assembler, disassembler, C compiler, interactive CLI, GUI) ships in one
  binary; subcommands select the mode (see the command reference in
  [implementation.md](implementation.md)). Phase 2 delivers `st16 vm
  prog.bin` (run via stdin/stdout) and `st16 cli [prog.bin]` (interactive
  debug REPL with `exit`).
- `tests/ST16.VM.Tests` —
  - unit tests for **all 37 instructions** (normal + edge cases: `NEG 0x8000`,
    `0x8000 / -1`, shift counts masked, sign of `MOD`),
  - full fault matrix (all 7 fault codes × triggering instruction),
  - spec §9 example programs verified **word for word** (binary encoding and
    final stack/memory state).

**Definition of done:** every normative behavior in spec §12 is asserted by
tests; `st16 vm` / `st16 cli` run the spec's examples and can single-step
them.

---

## Phase 3 — Assembler, Disassembler & Debug Symbols

The toolchain front end, plus the **debug symbol format** that makes the
combined source + disassembly view possible in the GUI.

**Deliverables**

- `src/ST16.Assembler` —
  - **Assembler** per spec §8: two-pass label resolution, all directives
    (`.org`, `.dw`, `.db`, `.ds`), line-numbered diagnostics (undefined label,
    duplicate label, operand/branch range errors, overlap detection). Emits:
    - `*.bin` — flat file of little-endian 16-bit words, and
    - `*.st16d` — debug file (format below).
  - **Disassembler** — `*.bin` → annotated assembly: offsets, mnemonics,
    operand interpretation, branch targets resolved to labels when a `.st16d`
    is available, cross-reference hints (call sites ↔ functions, jumps ↔
    labels).
- **`*.st16d` debug file** (JSON, self-contained, source embedded — format v1):
  - `sources[]` — each source file **with its full text embedded** (homework
    packets are one folder; nothing breaks when files move);
  - `lines[]` — address → (file, line) mapping, one entry per instruction and
    per data word (drives PC → source-line highlighting);
  - `symbols[]` — name, address, kind (`label` / `function` / `data`);
  - `functions[]` — name, entry, end (drives call-stack display and
    Step-Over/Step-Out frames);
  - `sections[]` — origin and length of the loaded image.
  - The **C compiler reuses this exact format**, referencing `.c` files
  instead of `.st16` files — one debug model for the whole toolchain.
- `st16` subcommands: `st16 as in.st16 -o out.bin -d out.st16d`,
  `st16 ds out.bin [-d in.st16d]`.
- `tests/ST16.Assembler.Tests` —
  - **round-trip identity:** assemble → disassemble → assemble produces a
    byte-identical binary,
  - spec §9 examples (encoding table verified against §9.3),
  - assembler error cases (line numbers, all diagnostic classes),
  - `.st16d` schema validation (every instruction address has a line entry).

**Definition of done:** for any PC, the toolchain can answer "which source
line, which instruction, which function" — everything the GUI's combined view
needs.

---

## Phase 3.5 — C Compiler (ST16-CC)

A real (but scoped) C-to-ST16 compiler so high-level code reaches the stack
machine. This is where "how does `for (…)` become stack shuffling" becomes
visible, and where the GUI's source view shows `.c` files instead of assembly.

**Deliverables**

- `src/ST16.CC` — `st16cc`, a C compiler for the **core C subset**:
  - **Types:** `int`, `short`, `char` (+ `unsigned` / `const` qualifiers).
    ST16 target profile: all integer types are 16-bit; `char` stored in the
    low 8 bits. `sizeof(int) == sizeof(short) == 2`.
  - **Expressions:** arithmetic, bitwise, shift, relational, logical,
    compound assignment, ternary, `++`/`--`, `sizeof`.
  - **Declarations:** local and global scalars and arrays (fixed size),
    `const`, initializers; function definitions with parameters; pointers to
    scalars and arrays.
  - **Control flow:** `if`/`else`, `while`, `do-while`, `for`, `break`,
    `continue`, `return`, `goto` (local labels only — useful and simple to
    lower).
  - **Not supported in v1** (each rejected with a clear, line-referenced
    diagnostic): structs, unions, floats, variadics, preprocessor macros
    (input is read as plain `.c`), multi-character literals, `long`/`double`
    (mapped to a "not supported in ST16 profile" error).
- **Code generation:**
  - Expression trees lower directly to stack ops — the teaching core of this
    phase; `-S` emits the intermediate `.asm` for inspection.
  - **Calling convention:** arguments pushed right-to-left on the operand
    stack; the callee cleans up; result returned on top of the stack (JVM-
    style, matches how `CALL`/`RET` already work).
  - **Frame model:** locals live in RAM in per-function frames allocated off a
    reserved stack-pointer word, so **recursion works** and the 32-slot
    operand stack is reserved for arguments, results, and short-lived
    expression temporaries. When temporaries would exceed the 32-slot limit,
    the compiler spills them to memory — and the emitted assembly shows the
    spill, which is a lesson in itself.
  - Emits `.asm` (with instruction → `.c`-line markers in `;` comments),
    runs the phase-3 assembler, then post-processes the assembler's
    `.st16d` (replaces `sources[]` with the `.c` text, re-maps `lines[]`
    through the markers): `st16 cc prog.c` → `prog.bin` + `prog.st16d`.
- **Tooling:** friendly, line-referenced parse and semantic error messages
  (students read these); `st16 cc` flags: `-o <out.s>` (stop at assembly),
  full pipeline by default.
- `tests/ST16.CC.Tests` —
  - **golden-assembly tests** for the key idioms (if/while/for lowering,
    array indexing, pointer arithmetic, recursion, function calls) so
    students can *read* what the compiler does,
  - **differential tests:** a corpus of C programs with reference output,
    compiled and executed on the VM with expected console output.

**Definition of done:** a recursive C program using arrays and pointers
compiles, runs correctly on the VM, and its `.st16d` maps every instruction
to a `.c` line — debuggable end-to-end.

---

## Phase 4 — Sample Programs

A growing library of programs that double as integration-test fixtures. Every
sample ships with source, prebuilt `.bin` (+ `.st16d`), and a README
(objective, expected console output, difficulty tag, discussion questions).

**Deliverables** (`samples/`, numbered by difficulty)

Assembly samples:
1. `hello` — console I/O, `STORE` to `0xF000`, `HALT`.
2. `sq` + call site — `CALL`/`RET` discipline (spec §9.1).
3. `starloop` — loop with RAM counter (spec §9.2).
4. `fib` — iterative and recursive variants; recursion shows the call stack.
5. `bubble` — bubble sort over a RAM array: pointers, loops, `LOAD`/`STORE`.
6. `calc` — small expression evaluator reading from console in (`0xF001`):
   the "wow" sample; exercises I/O, stack discipline, and subroutines.

C samples (via `st16cc`):
7. `factorial.c` — recursion, function calls.
8. `gcd.c` — loops + functions, small.
9. `stringrev.c` — pointers, arrays, strings.
10. `binsearch.c` — pointers, control flow, a "real" algorithm.
11. `guess.c` — console I/O loop, a tiny game.

Each sample's expected output is also an automated test (regression net for
VM, assembler, and compiler).

**Definition of done:** all samples build with one script, every README's
expected output matches an automated test, each sample maps to at least one
course topic.

---

## Phase 5 — Debug GUI (WPF) — **must have**

A WPF application over the phase-2 debug hooks and phase-3/3.5 debug files.
The centerpiece is the **combined source + disassembly window**: the source
view and the disassembly are one correlated pane — the current instruction
highlights its `.st16` line *or* its `.c` line, and hovering/disassembling
shows the live values.

**Deliverables**

- `src/ST16.Gui` (WPF, net10.0-windows):
  - **Load:** `.st16` source (assembles on load), `.c` + `.st16d`, or
    `.bin` + `.st16d`.
  - **Combined source + disassembly pane:**
    - PC-highlighted current line in source *and* disassembly;
    - breakpoints in the gutter (by line or address);
    - symbol navigation: click a label/function to jump to its definition;
      call-site cross-references ("find usages");
    - live value annotation on the current instruction (resolved operand,
      stack effect — the "aha" moment).
  - **Operand stack view:** visual 32-slot stack (top at top), slot numbers,
    hex + decimal, change highlighting vs. previous step.
  - **Call stack view:** frames with return address and resolved function
    name (from `functions[]`), click-to-jump.
  - **Memory view:** word-addressable hex grid (hex/decimal toggle), editable
    values, watchpoints on addresses.
  - **Console pane:** the VM's virtual I/O — captured output, input
    injection for `0xF001`, instruction counter and machine state always
    visible in a status bar.
  - **Run control toolbar:** Run, Pause, Step Into, Step Over, Step Out,
    Continue, Reset, **Undo** (phase-2 snapshot/restore), instruction-limit
    setting.
  - **Fault display:** fault code, PC, offending instruction, and the stack
    state at the fault — the machine's final "post-mortem" view.
- Integration tests (drives the VM directly, not the pixels): load each
  phase-4 sample, set a breakpoint at a known line, step to it, assert
  stack/memory state, continue to completion.

**Definition of done:** every phase-4 sample can be debugged start-to-finish
in the GUI (breakpoint → step → inspect → continue), including at least one
C program with `.c`-source highlighting.

---

## Phase 6 — Verilog Reference (someday)

**Not scheduled; tracked as a stretch goal.**

- Cycle-accurate Verilog implementation of spec v1.0 (same encoding, same
  fault behavior) so the spec can be proven against hardware, not just the VM.
- Testbench that replays the VM's instruction unit tests; waveforms for the
  spec §9 examples.
- Stretch: synthesize and run on a small FPGA board — a capstone-level
  milestone ("run your C program on real silicon").

---

## Cross-cutting decisions

- **Platform:** .NET 10 (LTS) — `net10.0`, C# 14 (Visual Studio 2026 CE,
  SDK 10.0.401). xUnit for tests. One solution, `ST16.slnx`, at the repo root.
- **Layout:**

  ```
  ST16/
  ├── plan.md
  ├── docs/                    # specs and documentation
  ├── src/
  │   ├── ST16.VM/             # phase 2 — engine + debug hooks (no UI deps)
  │   ├── ST16.Assembler/      # phase 3 — asm/disasm + .st16d
  │   ├── ST16.CC/             # phase 3.5 — C compiler
  │   ├── ST16.App/            # single st16.exe (vm/as/ds/cc/cli + GUI entry)
  │   └── ST16.Gui/            # phase 5 — WPF debugger (UI of st16.exe)
  ├── samples/                 # phase 4
  ├── tests/
  │   ├── ST16.VM.Tests/
  │   ├── ST16.Assembler.Tests/
  │   └── ST16.CC.Tests/
  └── rtl/                     # phase 6 (someday)
  ```

- **VM is host-agnostic:** `ST16.VM` has no UI dependency; the CLI, GUI, and
  tests are all thin consumers of its debug API.
- **Single executable:** one `st16.exe` (`ST16.App`) hosts the VM, assembler,
  disassembler, C compiler, interactive CLI, and GUI (no-arg default);
  subcommands select the mode. Engines stay separate, UI-free libraries.
- **Determinism:** everything is a pure function of (program, input).
  No threads, no clocks, no randomness in any engine component (GUI event
  threading is outside execution semantics).
- **Spec is frozen:** opcode table and semantics are v1.0. New features
  (trap vectors, more I/O devices, structs, byte addressing) go into a v1.1
  spec with a changelog before any phase implements them.

## Explicitly deferred (backlog)

| Item | Why deferred |
|---|---|
| Trap vectors / exception handlers | Spec v1.1; fault model in v1 is halt-and-report |
| Structs/unions/floats in `st16cc` | Core subset first; memory-layout teaching material grows a lot |
| More I/O devices (timer, framebuffer) | Console ports are enough for phases 2–5 |
| Byte addressing | Word-granular keeps the spec teachable; v1.1 candidate |
| Multi-cycle / pipelined timing model | One instruction per cycle keeps cycle counting honest |
