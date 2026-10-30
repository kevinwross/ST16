# ST16 Implementation Plan

Per-phase task checklists with definitions of done, plus a running log of
significant accomplishments. Complements [plan.md](plan.md) (what each phase
*delivers*) and [docs/ST16-ISA.md](docs/ST16-ISA.md) (the normative spec).

## Environment

- Microsoft Visual Studio 2026 Community Edition (installed on this machine).
- .NET SDK 10.0.401 (verified `dotnet --list-sdks`).
- **Target: .NET 10 (LTS) — `net10.0`, C# 14.** (plan.md originally said
  .NET 8; .NET 8 is EOL and the SDK here is 10 — plan.md corrected.)
- xUnit for tests; `dotnet test` is the gate for every task.

## Ground rules

- **Testing is important; regression testing is important.** The codebase is
  never left with failing tests — before any checkbox is ticked, the full
  `dotnet test` suite must pass, and each phase's definition of done includes
  the automated tests that keep the prior phases green.
- A checkbox is marked when the task is **done and its tests pass** — not
  when it is started.
- Significant accomplishments are appended to the [running log](#running-log)
  at the end of this file, timestamped.
- Log entry format (markdown):

  ```markdown
  ### YYYY-MM-DD HH:MM:SS ±ZZ:ZZ — <phase tag>: <short title>

  - **Status:** one or two lines on what now holds true.
  - **Changes:** what changed (files, behavior, design).
  - **Tests:** suite state — counts, coverage of the change.
  - **Remaining:** what is still open in this area.
  - **Next:** the immediate next step.
  ```

- Phase tags: `P1`, `P2`, `P3`, `P3.5`, `P4`, `P5`, `P6`.
- **Git:** local repository at the repo root. Commit after each completed
  checklist item (message `<phase>: <what>`); each log entry records the
  commit hash it refers to. Because the entry records its commit's hash, it
  is appended after the work commit and committed in a small follow-up
  commit.

---

## st16 command reference (single executable)

All functionality ships in one executable, **`st16`** (produced by
`dotnet publish`; dev mode via `dotnet run`); a subcommand selects the
mode. The engines stay separate, UI-free, testable libraries — only the
entry point is unified.

| Command | Description | Delivered in |
|---|---|---|
| `st16` | launch the debug GUI (default mode) | P5 |
| `st16 vm <file.bin>` | run the binary on the VM; console I/O via stdin/stdout | P2 |
| `st16 cli [file.bin]` | interactive debug CLI: REPL on stdin/stdout, `exit` to leave | P2 |
| `st16 as <file.st16> [-o out.bin] [-d out.st16d]` | assemble source to binary (+ debug file) | P3 |
| `st16 ds <file.bin> [-d in.st16d]` | disassemble to stdout (labels from `.st16d` when given) | P3 |
| `st16 cc <file.c> [-o out.s]` | compile C to assembly; without `-o`, full pipeline to `.bin` + `.st16d` | P3.5 |

---

## Phase 1 — ISA Specification

**Definition of done:** spec reviewed and approved; opcode table frozen.

- [x] Write normative v1.0 spec (`docs/ST16-ISA.md`): machine model, memory map, encoding, all 41 instructions, fault model, I/O ports, assembly syntax, §12 implementer summary
- [x] Review amendment: stack depths 16 → 32 (operand + call), configurable in implementations (spec §2.1, §10; no encoding impact)
- [x] Final user sign-off on spec v1.0 (signed 2026-10-29 — ISA frozen at v1.0)

---

## Phase 2 — Virtual Machine (ST16.VM + ST16.App)

**Definition of done:** every normative behavior in spec §12 is asserted by
tests; `st16 vm` and `st16 cli` run the spec's example programs and can
single-step them.

### Core engine

- [ ] Scaffold `ST16.sln`: `src/ST16.VM` (class library, no UI deps), `src/ST16.App` (the `st16` executable), `tests/ST16.VM.Tests` (xUnit); baseline `dotnet test` green
- [ ] 16-bit word value type: two's complement helpers (sign-extend 10-bit, compare signed/unsigned, wrap arithmetic)
- [ ] Memory: 61,440 words at `0x0000`–`0xEFFF`, zero-filled on reset; port range `0xF000`–`0xFFFF`
- [ ] Configurable stacks: `OperandStack` + `CallStack`, default 32 slots, configurable depth (4–256), underflow/overflow faults
- [ ] Machine state & configuration: `Running` / `Paused` / `Faulted` / `Halted`; stack depths; instruction-limit watchdog
- [ ] Decoder: 6-bit opcode table (41 instructions), 1-word/2-word formats, operand interpretation (ignored / signed 10-bit / relative / absolute), `ILLOP` fault on reserved opcodes
- [ ] Execute engine: fetch/decode/execute for all 41 instructions; atomic fault semantics (faulting instruction has no partial effects); 7 fault codes per spec §6
- [ ] I/O ports: `0xF000` console out (low 8 bits), `0xF001` console in (non-blocking, `0xFFFF` when empty), `0xF002` instruction counter; `IInputSource`/`IOutputSink` abstractions so CLI/GUI/tests supply I/O

### Debug hooks (the phase-5 GUI consumes this API)

- [ ] Execution events: `InstructionCompleted` (index, PC, decoded instruction), `FaultOccurred` (code, PC), `Halted` (termination code), `BreakpointHit` (address)
- [ ] Breakpoints: set/clear by address (multiple); break-on-fault-code; break-on-HALT
- [ ] Stepping: `StepInto`, `StepOver` (frame-aware across `CALL`), `StepOut`
- [ ] Run control: `Run`, `RunUntilBreakpoint`, `Reset`; instruction-limit watchdog stops runaway loops
- [ ] State inspection: PC, machine state, instruction counter, full 32-slot operand stack, call stack (return addresses + depth), memory windows, port values
- [ ] Snapshot/restore of complete machine state (powers GUI undo)

### st16 executable — P2 subcommands

- [ ] `ST16.App` project: single entry point (assembly name `st16`), subcommand routing, `--help`/version; no-arg `st16` prints a "GUI lands in P5" notice for now
- [ ] `st16 vm <file.bin>` — run the binary on the VM with stdin/stdout wired to the console ports
- [ ] `st16 cli [file.bin]` — interactive debug CLI: an interpreter loop on stdin/stdout (prompt → command → execute → loop back) with an `exit` command
- [ ] CLI commands: `load <file.bin>`, `run`, `step` (into / over / out), `dump` (stack / memory / call stack), breakpoint set/clear, instruction limit, `exit`

### Tests

- [ ] Unit tests for **all 41 instructions** incl. edge cases: `NEG 0x8000`, `0x8000 / -1`, shift-count masking, `MOD` sign, unsigned comparisons above `0x8000`
- [ ] Full fault matrix: all 7 codes × triggering instructions; atomicity (no partial effects after a fault)
- [ ] Spec §9 examples verified **word for word**: binary encoding (§9.3) and final stack/memory state
- [ ] Snapshot/restore round-trip; determinism (same program + input ⇒ identical trace)
- [ ] Configurable-depth behavior: strict mode (depth 4) faults where 32 does not

---

## Phase 3 — Assembler, Disassembler & Debug Symbols

**Definition of done:** for any PC, the toolchain answers "which source line,
which instruction, which function"; assemble → disassemble → assemble is
byte-identical.

### Assembler

- [ ] `src/ST16.Assembler` project; lexer: labels, mnemonics (case-insensitive), decimal/`0x`/`0b` literals, directives, `;` comments, line/column tracking
- [ ] Parser for spec §8 syntax: `[label:] mnemonic [operand]`
- [ ] Two-pass assembly: label resolution; expressions (constant, label, `label ± constant`)
- [ ] Range checks: `PUSH` −512…511; `JZ`/`JNZ` offset ±511 (relative to `PC+1`); `PUSH16`/`JMP`/`CALL` 0…65535
- [ ] Diagnostics with line numbers: undefined label, duplicate label, operand out of range, branch offset out of range, `.org` overlap, beyond RAM
- [ ] `.bin` output: little-endian 16-bit words
- [ ] `.st16d` v1 (JSON, self-contained, source embedded): `sources[]` (full text), `lines[]` (address → file/line, one per instruction and data word), `symbols[]` (label/function/data), `functions[]` (entry, end), `sections[]` (origin, length)

### Disassembler

- [ ] Binary → annotated assembly: offsets, mnemonics, operand interpretation, branch targets resolved to labels when `.st16d` present, cross-reference hints (call sites ↔ functions, jumps ↔ labels)

### CLI

- [ ] `st16 as` subcommand: `st16 as in.st16 -o out.bin -d out.st16d`
- [ ] `st16 ds` subcommand: `st16 ds out.bin [-d in.st16d]`

### Tests

- [ ] Round-trip identity: assemble → disassemble → assemble produces a byte-identical binary
- [ ] Spec §9.3 encoding table verified instruction by instruction; §9 examples assemble to the documented words
- [ ] Assembler error cases: every diagnostic class, correct line numbers
- [ ] `.st16d` schema validation: every instruction address has a line entry; embedded source round-trips

---

## Phase 3.5 — C Compiler (st16cc)

**Definition of done:** a recursive C program using arrays and pointers
compiles, runs correctly on the VM, and its `.st16d` maps every instruction
to a `.c` line.

### Front end

- [ ] `src/ST16.CC` project; C lexer for the core subset: keywords, identifiers, 16-bit integer literals, char/string literals, all core-subset operators, line/column tracking
- [ ] Parser: declarations (local/global scalars + fixed-size arrays, `const`, initializers), function definitions, full expression precedence (arithmetic, bitwise, shift, relational, logical, compound assignment, ternary, `++`/`--`, `sizeof`), statements (`if`/`else`, `while`, `do-while`, `for`, `break`, `continue`, `return`, `goto` with local labels)
- [ ] Semantic analysis: ST16 target profile (16-bit `int`/`short`, 8-bit `char` in low byte); scope rules; pointer/array compatibility; line-referenced diagnostics; clear "not supported in ST16 profile" errors for structs, unions, floats, variadics, macros

### Code generation

- [ ] Expression trees → stack ops; `-S` emits the intermediate `.asm`
- [ ] Calling convention: arguments pushed right-to-left on the operand stack; callee cleans up; result returned on top
- [ ] Frame model: reserved stack-pointer word in RAM; per-function RAM frames for locals; recursion supported
- [ ] Temp spilling to memory when the 32-slot operand stack limit is exceeded (spills visible in `-S` output)
- [ ] `.st16d` emission reusing the phase-3 format: `.c` source embedded, instruction → `.c`-line mappings (via `.asm` line markers); `functions[]` for call-stack display

### CLI & tests

- [ ] `st16 cc` subcommand: `st16 cc prog.c -o prog.s` (stop at assembly) or `st16 cc prog.c` (full pipeline → `prog.bin` + `prog.st16d`)
- [ ] Golden-assembly tests for the key idioms: `if`/`while`/`for` lowering, array indexing, pointer arithmetic, recursion, function calls
- [ ] Differential corpus: C programs with reference console output, compiled and executed on the VM
- [ ] Diagnostic tests: unsupported-feature rejections name the construct and line

---

## Phase 4 — Sample Programs

**Definition of done:** one script builds all samples; every README's
expected output matches an automated test; each sample maps to at least one
course topic.

### Assembly samples

- [ ] `samples/` layout + `build-all` script (assembles every sample)
- [ ] `hello` — console I/O, `STORE 0xF000`, `HALT`
- [ ] `sq` + call site — `CALL`/`RET` discipline (spec §9.1)
- [ ] `starloop` — loop with RAM counter (spec §9.2)
- [ ] `fib` — iterative and recursive variants (recursive shows the call stack)
- [ ] `bubble` — bubble sort over a RAM array (pointers, loops, `LOAD`/`STORE`)
- [ ] `calc` — expression evaluator reading console in `0xF001` (I/O, subroutines, stack discipline)

### C samples (via st16cc)

- [ ] `factorial.c` — recursion, function calls
- [ ] `gcd.c` — loops + functions
- [ ] `stringrev.c` — pointers, arrays, strings
- [ ] `binsearch.c` — pointers, control flow
- [ ] `guess.c` — console I/O loop, a tiny game

### Docs & tests

- [ ] Per-sample README: objective, expected console output, difficulty tag, discussion questions
- [ ] Automated expected-output test for every sample (regression net for VM, assembler, and compiler)

---

## Phase 5 — Debug GUI (WPF) — must have

**Definition of done:** every phase-4 sample can be debugged start-to-finish
in the GUI (breakpoint → step → inspect → continue), including at least one
C program with `.c`-source highlighting.

- [ ] `src/ST16.Gui` (WPF, net10.0) skeleton over the `ST16.VM` debug API (VM stays UI-free)
- [ ] Load pipeline: `.st16` (assemble on load), `.c` + `.st16d`, `.bin` + `.st16d`
- [ ] Combined source + disassembly pane: PC-highlighted current line in **both** views; gutter breakpoints (line or address); symbol navigation (label/function → definition); cross-references (call sites); live value annotation on the current instruction (resolved operand + stack effect)
- [ ] Operand stack view: visual 32-slot stack (top at top), slot numbers, hex + decimal, change highlighting vs. previous step
- [ ] Call stack view: frames with return address + resolved function name, click-to-jump
- [ ] Memory view: word-addressable hex grid, hex/decimal toggle, editable values, watchpoints
- [ ] Console pane: captured output, input injection for `0xF001`
- [ ] Run control toolbar: Run, Pause, Step Into, Step Over, Step Out, Continue, Reset, Undo (snapshot/restore), instruction-limit setting
- [ ] Fault display: code, PC, offending instruction, post-mortem stack state
- [ ] Status bar: PC, instruction counter, machine state
- [ ] Integration tests driving the VM + GUI logic layer (not pixels): load each phase-4 sample, breakpoint at a known line, step to it, assert stack/memory state, continue to completion

---

## Phase 6 — Verilog Reference (someday)

**Definition of done (when scheduled):** passes the same instruction test
corpus as the VM; waveforms available for the spec §9 examples.

- [ ] Cycle-accurate RTL of spec v1.0 (encoding, fault behavior, 32/32 reference stacks)
- [ ] Testbench replaying the VM's instruction unit tests
- [ ] Waveforms for the spec §9 examples
- [ ] *(Stretch)* synthesize and run on a small FPGA board — capstone milestone

---

## Deferred backlog (see plan.md)

No checkboxes — these are out of scope until a spec/plan change lands:
trap vectors (spec v1.1), structs/unions/floats in `st16cc`, extra I/O
devices, byte addressing, multi-cycle timing model.

---

## Running log

Most recent entries first. Entries are appended at significant
accomplishments (a completed checklist task or a decision worth remembering).

---

### 2026-10-29 22:00:42 -07:00 — P1: implementation.md created

- **Status:** implementation.md now defines per-phase task checklists, definitions of done, and this running log. Environment verified on this machine.
- **Changes:** new `implementation.md`; verified toolchain (Visual Studio 2026 CE, .NET SDK 10.0.401); corrected plan.md's platform target from .NET 8 to .NET 10 LTS (`net10.0`, C# 14) — .NET 8 is EOL and not installed here.
- **Tests:** n/a — no code yet. Ground rule set: `dotnet test` must pass before any checkbox is ticked.
- **Remaining:** P1 user sign-off on the spec; all P2+ tasks.
- **Next:** scaffold `ST16.sln` and start P2 core (word type, memory, configurable stacks).
