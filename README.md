# ST16

A 16-bit, zero-address (stack) **virtual CPU for teaching computer
architecture**. Every value — data, addresses, branch targets — lives on a
32-slot operand stack. No registers, no flags: the machine is a program
counter, two stacks, word-addressed memory, and memory-mapped I/O ports.

## Documentation

| Document | Purpose |
|---|---|
| [docs/ST16-ISA.md](docs/ST16-ISA.md) | Normative ISA specification v1.0 (**frozen**). Source of truth. |
| [plan.md](plan.md) | What each phase delivers, definitions of done, design rationale. |
| [implementation.md](implementation.md) | Per-phase task checklists, the `st16` command reference, and the running implementation log. |

## Requirements

- Windows with Visual Studio 2026 Community Edition (or any environment with
  the .NET 10 SDK — this project builds with SDK 10.0.401).
- `dotnet` CLI on the PATH.

## Build & test

```powershell
dotnet build ST16.slnx --nologo
dotnet test  ST16.slnx --nologo
```

The codebase is never left with failing tests; every task lands with the full
suite green.

## The st16 executable

All functionality ships in one executable, `st16` (in development:
`dotnet run --project src/ST16.App -- <subcommand>`).

| Command | Description | Status |
|---|---|---|
| `st16` | launch the debug GUI (default mode) | Phase 5 |
| `st16 vm <file.bin>` | run the binary on the VM; console I/O via stdin/stdout | Phase 2 |
| `st16 cli [file.bin]` | interactive debug CLI (REPL; `exit` to leave) | Phase 2 |
| `st16 as <file.st16> [-o out.bin] [-d out.st16d]` | assemble | Phase 3 |
| `st16 ds <file.bin> [-d in.st16d]` | disassemble to stdout | Phase 3 |
| `st16 cc <file.c> [-o out.s]` | compile C to assembly (or full pipeline) | Phase 3.5 |

## Repository layout

```
ST16/
├── README.md
├── plan.md
├── implementation.md
├── docs/ST16-ISA.md
├── src/
│   ├── ST16.VM/        # VM engine + debug hooks (no UI, no external packages)
│   ├── ST16.Assembler/ # assembler, disassembler, .st16d debug format
│   ├── ST16.CC/        # C-subset compiler (emits ST16 assembly)
│   ├── ST16.App/       # the single st16 executable (vm/as/ds/cc/cli + GUI entry)
│   └── ST16.Gui/       # WPF debugger (UI of st16.exe)
├── samples/            # numbered sample programs (Phase 4)
└── tests/              # xUnit test projects, one per library
```

## Design in one paragraph

ST16 is a pure stack machine: 37 instructions, a 6-bit opcode in the high
byte (bits 15–14 reserved zero) plus an 8-bit operand field, 16-bit words,
two's-complement wrap-around arithmetic, 7 fault codes
(halt-and-report, no traps in v1), and a 128 KiB word-addressed address space
with the top 8 KiB reserved for I/O ports (console out `0xF000`, console in
`0xF001`, instruction counter `0xF002`). Stack depths default to 32 slots and
are configurable down to 4 for strict-mode stack-discipline exercises. The
ISA spec is frozen at v1.0; extensions land in v1.1 with a changelog.
