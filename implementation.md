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
- create a local git repository for checkpointing progress. Checkpoint at the end of each checklist or phase completion.

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
- The AI model can handle up to 8 subagents if you think that will help

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

- [ ] Write normative v1.0 spec (`docs/ST16-ISA.md`): machine model, memory map, encoding, all 37 instructions, fault model, I/O ports, assembly syntax, §12 implementer summary
- [ ] Review amendment: stack depths 16 → 32 (operand + call), configurable in implementations (spec §2.1, §10; no encoding impact)
- [ ] Final user sign-off on spec v1.0 (signed 2026-10-29 — ISA frozen at v1.0)

---

## Phase 2 — Virtual Machine (ST16.VM + ST16.App)

**Definition of done:** every normative behavior in spec §12 is asserted by
tests; `st16 vm` and `st16 cli` run the spec's example programs and can
single-step them.

### Core engine

- [x] Scaffold `ST16.slnx`: `src/ST16.VM` (class library, no UI deps), `src/ST16.App` (the `st16` executable), `tests/ST16.VM.Tests` (xUnit); baseline build green (0 warnings / 0 errors)
- [x] 16-bit word value type: two's complement helpers (sign-extend 8-bit per erratum, compare signed/unsigned, wrap arithmetic)
- [x] Memory: 61,440 words at `0x0000`–`0xEFFF`, zero-filled on reset; port range `0xF000`–`0xFFFF`
- [x] Configurable stacks: `OperandStack` + `CallStack`, default 32 slots, configurable depth (4–256), underflow/overflow faults
- [x] Machine state & configuration: `Running` / `Paused` / `Faulted` / `Halted`; stack depths; instruction-limit watchdog
- [x] Decoder: 6-bit opcode table (37 defined per the frozen §5 table), 1-word/2-word formats, operand interpretation (ignored / signed 8-bit / relative / absolute), `ILLOP` fault on reserved opcodes and on bits 15–14 set
- [x] Execute engine: fetch/decode/execute for all 37 instructions; atomic fault semantics (faulting instruction has no partial effects); 7 fault codes per spec §6
- [x] I/O ports: `0xF000` console out (low 8 bits), `0xF001` console in (non-blocking, `0xFFFF` when empty), `0xF002` instruction counter; `IInputSource`/`IOutputSink` abstractions so CLI/GUI/tests supply I/O

### Debug hooks (the phase-5 GUI consumes this API)

- [x] Execution events: `InstructionCompleted` (index, PC, decoded instruction), `FaultOccurred` (code, PC), `Halted` (termination code), `BreakpointHit` (address)
- [x] Breakpoints: set/clear by address (multiple); v1 faults and HALT are always terminal (reserved `BreakOnFaultEnabled`/`BreakOnHalt` properties for v1.1 trap vectors)
- [x] Watchpoints: set/clear by address (multiple); `WatchpointHit` event (write-only: fires on `STORE` to a watched RAM or port address)
- [x] Stepping: `StepInto`, `StepOver` (frame-aware across `CALL`), `StepOut`
- [x] Run control: `Run` (stops at breakpoint/fault/limit/HALT), `Reset`; instruction-limit watchdog stops runaway loops
- [x] State inspection: PC, machine state, instruction counter, full 32-slot operand stack, call stack (return addresses + depth), memory windows, port values
- [x] Snapshot/restore of complete machine state (powers GUI undo)

### st16 executable — P2 subcommands

- [x] `ST16.App` project: single entry point (assembly name `st16`), subcommand routing, `--help`/version; no-arg `st16` prints a "GUI lands in P5" notice for now
- [x] `st16 vm <file.bin>` — run the binary on the VM with stdin/stdout wired to the console ports (exit codes 0 HALT / 1 FAULT / 2 usage / 3 limit)
- [x] `st16 cli [file.bin]` — interactive debug CLI: an interpreter loop on stdin/stdout (prompt → command → execute → loop back) with an `exit` command
- [x] CLI commands: `load <file.bin>`, `run`, `step` (into / over / out), `dump` (stack / memory / call stack), breakpoint set/clear, instruction limit, `exit` (+ `in`, `state`, `help`)

### Tests

- [x] Unit tests for **all 37 instructions** incl. edge cases: `NEG 0x8000`, `0x8000 / -1`, shift-count masking, `MOD` sign, unsigned comparisons above `0x8000`
- [x] Full fault matrix: all 7 codes × triggering instructions; atomicity (no partial effects after a fault)
- [x] Spec §9 examples verified **word for word**: 25-word starloop encoding (§9.3, post-erratum) executed to the expected 10-asterisk output, HALT, no fault
- [x] Snapshot/restore round-trip; determinism (same program + input ⇒ identical trace)
- [x] Configurable-depth behavior: strict mode (depth 4) faults where 32 does not

---

## Phase 3 — Assembler, Disassembler & Debug Symbols

**Definition of done:** for any PC, the toolchain answers "which source line,
which instruction, which function"; assemble → disassemble → assemble is
byte-identical.

### Assembler

- [x] `src/ST16.Assembler` project; lexer: labels, mnemonics (case-insensitive), decimal/`0x`/`0b` literals, directives, `;` comments, line/column tracking
- [x] Parser for spec §8 syntax: `[label:] mnemonic [operand]`
- [x] Two-pass assembly: label resolution; expressions (constant, label, `label ± constant`)
- [x] Range checks (per erratum): `PUSH` −128…127; `JZ`/`JNZ` offset ±127 (relative to `PC+1`, hard reject — no wrap); `PUSH16`/`JMP`/`CALL` 0…65535
- [x] Diagnostics with line numbers: undefined label, duplicate label, operand out of range, branch offset out of range, `.org` overlap, beyond RAM
- [x] `.bin` output: little-endian 16-bit words
- [x] `.st16d` v1 (JSON, self-contained, source embedded): `sources[]` (full text), `lines[]` (address → file/line, one per instruction and data word; two-word instructions map the header address), `symbols[]` (label/function/data; a label targeted by `CALL` is a `function`), `functions[]` (entry, end), `sections[]` (origin, length)

### Disassembler

- [x] Binary → annotated assembly: offsets, mnemonics, operand interpretation, branch targets resolved to labels when `.st16d` present, cross-reference hints (call sites ↔ functions, jumps ↔ labels)

### CLI

- [x] `st16 as` subcommand: `st16 as in.st16 -o out.bin -d out.st16d` (defaults: inputs' directory, `<name>.bin` / `<name>.st16d`)
- [x] `st16 ds` subcommand: `st16 ds out.bin [-d in.st16d]`

### Tests

- [x] Round-trip identity: assemble → disassemble → assemble produces a byte-identical binary
- [x] Spec §9.3 encoding table verified instruction by instruction (post-erratum); §9 examples assemble to the documented words
- [x] Assembler error cases: every diagnostic class, correct line numbers
- [x] `.st16d` schema validation: every instruction address has a line entry; embedded source round-trips

---

## Phase 3.5 — C Compiler (st16cc)

**Definition of done:** a recursive C program using arrays and pointers
compiles, runs correctly on the VM, and its `.st16d` maps every instruction
to a `.c` line.

### Design contract (settled 2026-10-30)

**Target profile.** `int`/`short` are 16-bit signed; `char` is signed and
stored in the low 8 bits of a word (sign-extended on load: `PUSH 8; SLL;
PUSH 8; SLR`); `unsigned char` loads are masked (`PUSH16 0x00FF; AND`).
Every object (scalar or array element, incl. `char`) occupies exactly one
16-bit word, so pointer arithmetic is word-granular. `sizeof` reports the C
standard values (`int`/`short` = 2, `char` = 1); layout is uniformly
1 word/object (documented divergence, like a 16-bit C target).
**Literal natural type:** an integer literal whose value only fits in
0x8000..0xFFFF (e.g. `60000`) has natural type `unsigned int` — the only
16-bit type that holds it — which selects unsigned comparisons and the
software unsigned division below; storing it into `int` is a compile error
(`constant 60000 does not fit in type 'int'`).

**Memory layout (fixed).**

| Region | Address | Contents |
|---|---|---|
| code | 0x0000… | entry stub, functions |
| free | …0xDFFF | — |
| frame area | ≤ 0xDFFF | grows down from 0xDFFF |
| SP word | 0xE000 | frame top; initial value 0xE000 (a data word) |
| globals | 0xE001…0xEFFF | globals + string literals, then 2 compiler-reserved scratch words (`st16cc_scratch`: unsigned-div quotient + divisor; see below) |

Overflow of code (>0x9FFF) or globals (>0xEFFF) is a compile error.

**Entry stub.** The compiler emits, at 0x0000: `CALL main; HALT` (main's
return value is left on the stack and discarded). Missing `main` or a
`main` with parameters is an error.

**Calling convention (JVM-style, caller stores arguments).** The **caller**
evaluates each argument (left-to-right) and stores it directly into the
callee's frame — argument *i* (0-based) at `old-SP − (calleeFrame − i)`,
i.e. the callee's slot *i* — then `CALL`s (two words: `0x2000`, target).
The callee's prologue lowers SP by the full frame, so the arguments are
already in slots 0..p−1; nothing is popped. The result is returned on top
of the operand stack (RET pops the call stack only). Every function's frame
starts with its parameter slots (slot 0 = first parameter), so `&param` is
legal.

**Frame model.** Per-function RAM frame allocated off the SP word at 0xE000.
`frameSize = params + locals + spillBudget` (16); slot 0..p−1 = parameters
(stored by the caller), p..p+l−1 = locals, p+l.. = spill slots — the whole
frame is what the prologue reserves, so a callee's spill slots never
overlap the caller's frame. Prologue: `SP ← [SP] − frameSize`. Epilogue /
every `return`: `SP ← [SP] + frameSize` (old SP = new SP + frameSize — no
saved slot needed), then RET.
`LOAD local i` = `PUSH16 0xE000; LOAD; PUSH i; ADD; LOAD`;
`STORE local i` = (value on stack) `PUSH16 0xE000; LOAD; PUSH i; ADD; STORE`.
Globals are direct: `PUSH16 <addr>; LOAD` / `PUSH16 <addr>; STORE`.

**VM operand semantics the codegen relies on** (verified against
`ST16Machine`/`Word`): all binary arithmetic/comparisons read `wa` = second
and `wb` = top, so `Emit(L); Emit(R)` gives `L op R` directly; comparisons
**pop both operands** and push 1/0; `SUB` = wa − wb; `DIV`/`MOD` =
truncated **signed** wa ÷ wb (divisor = top); `SLL`/`SLR` take the value
on top and the count second (codegen SWAPs); `STORE` consumes
(second = value, top = address); `JZ`/`JNZ` pop the condition. Because the
VM's division is signed-only, **unsigned `/` and `%` on values ≥ 0x8000
lower to a software subtraction loop** (while u ≥ v: u −= v, q++) with the
quotient and divisor kept in the two `st16cc_scratch` words; division by
zero is undefined (documented UB, loops forever).

**Operand-stack spilling.** The codegen tracks current stack depth; max
allowed depth is 28 (safety margin under the VM's 32). Before pushing, if
the depth limit would be exceeded the oldest stack value is spilled to the
function's next spill slot (`STORE`) and reloaded (`LOAD`) exactly when
that depth level is reached again — visible in `-S` output as
`PUSH16 0xE000; LOAD; PUSH <slot>; ADD; STORE/LOAD` pairs. Spill slot
numbers start at p + l (after the parameter and local slots) and the base
is per-function. When a *second* (older) operand is reloaded for a binary
op it is loaded and then `SWAP`ed back under the live top so the
`wa op wb` ordering holds.

**Line markers → .st16d post-process.** The compiler emits `.asm` with a
marker comment on its own line, `; L<line>`, before each statement/
expression chunk of C line `<line>`. It runs the phase-3 assembler on that
`.asm` (fileName `gen.st16`), then post-processes the resulting `.st16d`:
`sources[]` ← the `.c` file (name + full text); each `lines[]` entry
(address → asm line) is re-mapped to the C line of the nearest preceding
`L` marker; `symbols[]`/`functions[]`/`sections[]` carry through unchanged
(function labels are the C names, so call-stack display works).

**Console I/O builtins.** `putchar(int c)` → `PUSH <c>; PUSH16 0xF000;
STORE` (low byte written); `getchar()` → `PUSH16 0xF001; LOAD` (byte, or
0xFFFF when input is exhausted). No header declaration needed; any other
"library" call is an undefined-symbol error.

### Front end

- [x] `src/ST16.CC` project; C lexer for the core subset: keywords, identifiers, 16-bit integer literals, char/string literals, all core-subset operators, line/column tracking
- [x] Parser: declarations (local/global scalars + fixed-size arrays, `const`, initializers), function definitions, full expression precedence (arithmetic, bitwise, shift, relational, logical, compound assignment, ternary, `++`/`--`, `sizeof`), statements (`if`/`else`, `while`, `do-while`, `for`, `break`, `continue`, `return`, `goto` with local labels)
- [x] Semantic analysis: ST16 target profile (16-bit `int`/`short`, 8-bit `char` in low byte); scope rules; pointer/array compatibility; line-referenced diagnostics; clear "not supported in ST16 profile" errors for structs, unions, floats, variadics, macros

### Code generation

- [x] Expression trees → stack ops; `-S` emits the intermediate `.asm`
- [x] Calling convention: arguments stored by the caller into the callee's frame slots (right-to-left frame layout, slot 0 = first parameter); result returned on top
- [x] Frame model: reserved stack-pointer word in RAM; per-function RAM frames for locals; recursion supported
- [x] Temp spilling to memory when the 32-slot operand stack limit is exceeded (spills visible in `-S` output)
- [x] `.st16d` emission reusing the phase-3 format: the compiler emits line markers as `;` comments in the `.asm`, runs the phase-3 assembler, and post-processes the resulting `.st16d` (replaces `sources[]` with the `.c` text, re-maps `lines[]` through the markers); `functions[]` for call-stack display

### CLI & tests

- [x] `st16 cc` subcommand: `st16 cc prog.c -o prog.s` (stop at assembly) or `st16 cc prog.c` (full pipeline → `prog.bin` + `prog.st16d`)
- [x] Golden-assembly tests for the key idioms: `if`/`while`/`for` lowering, array indexing, pointer arithmetic, recursion, function calls
- [x] Differential corpus: C programs with reference console output, compiled and executed on the VM
- [x] Diagnostic tests: unsupported-feature rejections name the construct and line

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

**Design reference (owner, 2026-10-30):** the owner pointed at a debugger
they have enjoyed from another project (RISC-V-class debugger screenshot) as
a layout to emulate — not identical, but a good starting point. Notable
elements to carry over where they fit ST16:

- top toolbar: Load / Run / Break / Restart / Reload + a "step at reset"
  toggle and a step-unit selector (instruction steps for ST16)
- two side-by-side stepping panes, each with its own **Over / In / Out**
  buttons: for ST16, *disassembly* (instruction steps, always) and *C
  source / `.st16` assembly* (line steps where debug info exists)
- memory pane (hex grid with PTR/HEX/ASCII columns), registers pane (for
  ST16: PC, instruction counter, machine state, last fault — a "registers"
  row rather than a file-register table), variables/watchpoints pane
  (globals + locals resolved from `.st16d`), call stack pane (frame,
  label, line), console pane
- bottom status bar: `pc=…`, current source line, instruction count,
  elapsed/IPS-style counters, and a run/break key hint (F5 run, F6 break
  style)

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

### 2026-10-31 01:30:02 -07:00 — P3.5: st16cc complete (60-test suite; `.c` → `.bin` + `.st16d`)

- **Status:** Phase 3.5 is done — `st16 cc prog.c` compiles the core-C subset to ST16 assembly, assembles it, and post-processes the `.st16d` so every instruction maps to a `.c` line; a recursive C program with arrays and pointers compiles, runs correctly on the VM, and is debuggable end-to-end. All P3.5 checkboxes are ticked; plan table 3.5 → ✅.
- **Changes:** `src/ST16.CC/` (new): `CLexer` (keywords/idents/16-bit int + char/string literals with escapes, three-char operators, line/column tracking, >0xFFFF and multi-char-literal rejections), `CParser` (full precedence climbing; declarations with initializers incl. `{ … }` lists and inferred array sizes; prototypes; statements incl. `goto` with local labels; unsupported-keyword rejections), `CSemantics` (scopes, parameter/local slot numbering 0..p−1 then locals, arg-count checks against definitions **and** prototypes, const/assignability rules incl. the literal natural-type rule, return/void discipline, break/continue loop checks, global-constant initializers), `CStackManager` (depth tracking, spill/reload at the 28-slot limit, `EnsureTopLive(2)` reload+`SWAP` for a spilled second operand), `CCodegen` + `CCodegen.Expr` (frame prologue/epilogue off the SP word, caller-stores-arguments calling convention, char sign-extend/mask idioms, software unsigned `/`/`%` subtraction loop over two `st16cc_scratch` cells, `putchar`/`getchar` port builtins), `CCPipeline` (assemble + `.st16d` post-process: sources ← the `.c` text, lines remapped through `; L<n>` markers, symbols/functions carry through). `src/ST16.App/`: `st16 cc` subcommand (`CcCommand`; `-o` stops at `.s`; default full pipeline to `.bin` + `.st16d`). `tests/ST16.CC.Tests/` (new, 60 tests): `DifferentialTests` (14 programs compiled + executed on the real VM: recursion `fib(20)`, loops/break/continue, char/unsigned-char, pointers + nested calls, `getchar` input port, forward declarations, ternary/`sizeof`/`goto`/do-while, signed division truncation **and** the unsigned software loop, global array + string + const initializers, short-circuit evaluation), `StackManagerTests` (7: spill/reload idioms word-for-word, budget overflow), `LexerTests`, `SemanticsTests` (diagnostic messages with line info), `PipelineTests` (`.st16d` sources/lines remap, symbols, entry line), `GoldenTests` (pinned assembly for small programs + entry-stub word layout).
- **Bugs found and fixed this phase:** (1) unsigned division read the VM's **signed** `DIV` and literals above 0x7FFF got signed type → natural-type literal rule + software unsigned loop; (2) the loop's `UGE` consumes **both** operands, so the first draft faulted on an empty stack — the divisor is parked in a second scratch cell and the compare idiom `LOAD v; SWAP; DUP; ROT` rebuilds the operands each iteration; (3) array/scalar global initializers emitted `.dw 0061` (no `0x` prefix) so the assembler read decimal — string bytes were silently corrupted (`'a'` → 61); (4) `ParseTypeSpecifier` returned on `void` **without advancing**, so `void f() { }` parsed `void` as a variable name — the token is now consumed; (5) **latent frame clobber:** the spill-slot base was a constant (16) for every function, so a function with ≥2 params+locals placed spill slots outside its reserved frame (into the caller's frame); the manager is now created per function with base = params+locals and the frame is params+locals+spillBudget (a probe `fib(10)` exposed it after a frame-size change that double-counted parameters — reverted); (6) `CBlock.Line` was the first statement's line, not the brace's, and `CFuncDef.Line` followed it — function line markers (and `.st16d` entries) now use the header line.
- **Tests:** `ST16.CC.Tests` 60/60; full regression `ST16.VM.Tests` 136/136 and `ST16.Assembler.Tests` 141/141 unchanged.

---

### 2026-10-30 16:55:34 -07:00 — P3: assembler, disassembler, `st16 as`/`st16 ds`, and 141-test suite complete

- **Status:** Phase 3 is done — for any PC the toolchain answers "which source line, which instruction, which function", and assemble → disassemble → assemble is byte-identical (verified by test). All P3 checkboxes are ticked. `dotnet test` also works now (see environment note).
- **Changes:** `src/ST16.Assembler/` (new, `3acf1e8`): `St16Assembler` (two-pass: parse with line/column tracking, label registration + duplicate/undefined checks, `.org` evaluation with backwards/beyond-RAM/overlap rejection, then emission with per-instruction range checks and `.st16d` construction), `Expression` (constant / label / `label ± constant`, decimal/`0x`/`0b`, ASCII and U+2212 minus), `St16dModel` (v1 document + `ToJson`/`FromJson`). Conventions: two-word instructions map both words to the header line in `lines[]`; a `CALL`-targeted label is `function`, a label on a data word is `data`; `functions[].end` is exclusive (contiguous instruction run capped at the next function); `sections[]` are maximal contiguous written runs. `src/ST16.Disassembler/` (new, `3acf1e8`): decode by operand kind (PUSH signed decimal; JZ/JNZ absolute target; PUSH16/JMP/CALL value); unknown opcodes, non-zero reserved operand bytes, and dangling two-word headers emit verbatim `.dw`; zero runs → `.ds n` (DUP when the `.st16d` line map says code); symbol-name resolution and `CallReferences`/`BranchReferences` (JMP/JZ/JNZ only); `ToText()` is directly reassemblable. `src/ST16.App/` (`3acf1e8`): `as` (input → `<name>.bin` + `<name>.st16d`, `-o`/`-d`, exit 0/1/2) and `ds` (image → annotated assembly, `-d` for symbols, exit 0/2) subcommands.
- **Bugs found and fixed this phase:** (1) `ST16Machine.Run` re-stopped immediately at the PC of the breakpoint it had just stopped at, so a resume executed nothing — fixed with resume-skips-current-breakpoint semantics (the stop PC is skipped on the first fetch only; `0d6d410`, P2 suite grew to 136/136 with the stepping/breakpoint/snapshot coverage that exposed it); (2) disassembler recorded every two-word instruction (incl. PUSH16 data values) in `BranchReferences` — now JMP/JZ/JNZ only (`b1065dd`).
- **Tests:** `tests/ST16.Assembler.Tests/` (`247f26f`, 141 tests, 10 files): §9.3 encoding table for all 37 opcodes instruction-by-instruction; §9.1/§9.2 goldens word-for-word (starloop assembles to exactly the documented 25 words and runs on the VM to 10×`*`, Halted, ic 121, pc 0x0017); **round-trip byte-identity** (4 programs × with/without `.st16d`); every §8.4 diagnostic class with exact line:column and no-output-on-error; `.st16d` schema (source embedding, one line entry per written word incl. two-word header mapping, symbol kinds, sq function entry 3/end 6, `.org`-gap sections, `FromJson(ToJson())` round-trip); disassembler semantics (verbatim `.dw` cases, `.ds` runs, OrgLine, DUP via line map, cross-references, reassemblable `ToText`); directives/expressions (`.db 0x12,0x34 → 0x3412`, `label ± constant` both minus forms, lowercase mnemonics); multi-file assembly; CLI (Process-based, exit codes and default outputs); end-to-end.
- **Environment note (supersedes the P2 note):** `dotnet test` works on this machine with `-p:_MSTestEnableParentProcessQuery=false` (the VSTest testhost's parent-process query is denied by this host's process configuration; the property disables it). Gates: `dotnet build ST16.slnx` 0 warnings / 0 errors; ST16.VM.Tests 136/136; ST16.Assembler.Tests 141/141.
- **Remaining:** P3.5 (C compiler), P4 (samples), P5 (GUI).
- **Next:** Phase 3.5 — `ST16.CC` (st16cc): core-C-subset front end, stack codegen with the RAM-frame model and 32-slot spilling, `.st16d` post-processing, `st16 cc` subcommand.
- **Commits:** `3acf1e8` — P3: ST16.Assembler + ST16.Disassembler + st16 as/ds; `0d6d410` — P2: breakpoint-resume fix + test coverage (136/136); `b1065dd` — P3: BranchReferences fix; `247f26f` — P3: test suite (141 tests) (this log entry committed separately).

---

### 2026-10-30 13:13:50 -07:00 — P2: core engine, st16 executable, and test suite complete

- **Status:** Phase 2 is done. `ST16.VM` implements the full spec v1.0 engine (all 37 instructions, 7 fault codes with spec-§6 priority and atomic fault semantics, configurable 4–256 stack depths defaulting to 32/32, the I/O port model, snapshots, events, breakpoints, write-only watchpoints, and frame-aware stepping); `st16` ships the `vm` and `cli` subcommands; the P2 test suite is green (126/126). All P2 checkboxes are ticked.
- **Changes:** `src/ST16.VM/` (new, 12 files): `Word`, `FaultCode`(+names), `MachineState`, `Opcode` (37-entry table with mnemonic/two-word/operand/min-depth), `Io` (`IInputSource`/`IOutputSink` + queue/string implementations), `Memory` (61,440 words + port region), `St16Config` (validated depths), `DecodedInstruction`, `InstructionCompletedEventArgs`, `MachineSnapshot`, `FixedWordStack` (bottom-growth array, top-first public API), `ST16Machine` (load/reset, run, step into/over/out, instruction-limit watchdog, breakpoint/watchpoint management, state inspection, port access, snapshot/restore, the 5 events, single-step `ExecuteOne` with the full fault matrix). `src/ST16.App/` (new): `Program` (subcommand routing, `--help`/`--version`, no-arg GUI notice, exit 2), `VmCommand` (LE binary load, console ports, exit 0/1/2/3, `FAULT <NAME> at 0x…` diagnostics), `CliCommand` (REPL: load/run/step into|over|out/dump stack|memory|callstack/bp set|clear|list/limit/in/state/help/exit), `ConsoleIo` (Peek-based input, flushing output), `Numbers` (decimal/hex/binary parsing). `tests/ST16.VM.Tests/` (new, 7 files, 126 tests): `InstructionTests` (all 37 + edge cases), `FaultTests` (full 7-code matrix, atomicity via pre/post snapshots, priority order, strict-depth-4 overflow), `GoldenTests` (spec §9 starloop word-for-word — 10×`*`, HALT, ic 121, pc 0x0017, counter word 0; §9.1 sq layout → 49; breakpoint + step-over-CALL variants), `PortIoTests` (all §7 port semantics incl. exhaustion → 0xFFFF, ic low-16 wrap, watchpoint on port STORE), `DebugHookTests` (stepping, breakpoints, write-only watchpoints, limit watchdog, event streams, load/reset semantics), `SnapshotConfigTests` (mid-run round-trip, cross-instance restore, determinism traces, config boundaries and strict-depth behavior). `implementation.md`: P2 checkboxes ticked (24); P5 design-reference block (owner's reference debugger layout, 2026-10-30).
- **Bugs found and fixed this phase:** (1) decode mask tested operand-byte bits (`& 0xC0`) instead of header bits 15–14 (`& 0xC000`) — caused spurious `ILLOP`; (2) `FixedWordStack` accessors were bottom-anchored while the grow slot was the bottom — inverted top; (3) `RET` with an empty call stack crashed instead of raising `CALLUF` (found by the test suite); (4) missing `STACKOF` checks on the net-push instructions (PUSH/PUSH16/DUP/OVER/LOAD); (5) `st16 cli` `dump memory <addr> 1` crashed on a non-4-aligned count.
- **Tests:** 126/126 green. Note on harness: `dotnet test`/`dotnet vstest` abort in this environment before any test runs — the VSTest testhost fails with `Win32Exception (5)` (access denied) while setting its parent-process exit callback (`DefaultEngineInvoker.SetParentProcessExitCallback`), reproduced on every invocation incl. background jobs; this is an environmental process-handle restriction, not a test failure. The suite was therefore run in-process: a small throwaway runner (temp dir, not committed) loads `ST16.VM.Tests.dll`, discovers `[Fact]`/`[Theory]` members, and executes each case with the real xunit 2.9.3 asserts — 126/126 passed. The suite is standard xUnit and is expected to run under `dotnet test` on machines without the handle restriction. Additionally: full-solution build green (0 warnings / 0 errors) and CLI smoke passes (`st16 vm` on the spec §9.3 starloop binary → exactly 10 asterisks, exit 0; `--limit 5` → exit 3; `--help` → 0; no-arg → 2; REPL load/run/step/bp/dump/in all verified).
- **Remaining:** all P3+ tasks.
- **Next:** Phase 3 — `ST16.Assembler` (two-pass, §8 syntax, diagnostics), `.bin`/`.st16d` v1 output, `st16 as`/`st16 ds` subcommands, round-trip identity.
- **Commits:** `8f05fca` — P2: ST16.VM core engine (37 instructions, 7 faults, debug hooks); `f877e28` — P2: st16 executable (vm + cli REPL); `aebb7b3` — P2: VM test suite (126 tests: all 37 instructions, fault matrix, golden, ports, debug hooks, snapshots) (this log entry committed separately).

---

### 2026-10-30 09:27:25 -07:00 — P1: baseline reset + review fixes

- **Status:** Baseline re-based and the toolchain docs are now mutually consistent with the frozen spec. P2/P3 code was intentionally reset out of the working tree; the old implementation remains in history at `ef08947` as reference (verified green there: VM 78/78, Assembler 117/117) — P2/P3 will be re-implemented from scratch against the corrected baseline.
- **Changes:** Spec (docs/ST16-ISA.md): port-region instruction fetch is now ADDRF (§3, §6, §12); `JZ`/`JNZ` range reconciled to ±127 in the §4/§8.2 body (−128 hard-rejected, wrap rationale); `DIV` effect corrected to `trunc(a÷b)` (the floor symbol contradicted "truncated toward zero"); `OVER` added to the §6 STACKOF cause list; fault-priority rule added (ILLOP → STACKUF → DIV0/ADDRF → STACKOF, effect order); input-exhaustion rule for `0xF001` → `0xFFFF` (§7); §9.2 stack-trace comment corrected (loop top holds `[ch]` only); status header now "Frozen (signed 2026-10-29; errata 2026-10-30)". plan.md: 37 instructions (both mentions), terminal-fault breakpoint policy, write-only watchpoints hook added to P2 debug hooks, C `.st16d` post-processing pipeline specified (P3.5), `ST16.slnx`, WPF target `net10.0-windows`, determinism rule scoped to engine components. README.md: 37 instructions with correct opcode/operand wording (6-bit opcode in high byte, 8-bit operand field), `ST16.slnx` build commands. implementation.md: P1 item says 37, P2 watchpoints checklist item added, `.st16d` conventions stated (two-word instructions map the header address; a `CALL`-targeted label is a `function`), P3.5 pipeline mechanism specified. Working tree: P2/P3 code, samples, `ST16.slnx`, `.gitignore` deleted (intentional reset); P1 checkboxes left unchecked and log emptied by the reset, per owner decision.
- **Tests:** n/a — docs only; no code in the working tree after the reset. Reference implementation at `ef08947` re-verified green in a throwaway worktree (78/78 VM, 117/117 Assembler, SDK 10.0.401), then removed.
- **Remaining:** all P2+ tasks. P2 scaffold and core engine are next.
- **Next:** scaffold `ST16.slnx` (`ST16.VM`, `ST16.App`, `tests/ST16.VM.Tests`), then the P2 core: word type, memory, configurable stacks, decoder, execute engine.
- **Commit:** `2e83255` — P1: baseline reset + review fixes (spec clarifications, doc consistency) (this log entry committed separately).

