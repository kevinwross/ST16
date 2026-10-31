# ST16 samples

Eleven teaching programs, in increasing order of what they exercise.
Each directory has its own `README.md` with the objective, expected
console output, and discussion questions.

| # | Sample | Language | Exercises | Output |
|---|--------|----------|-----------|--------|
| 01 | hello | assembly | RAM strings, index loop, `JZ` | `Hello, ST16!` |
| 02 | sq | assembly | functions, `CALL`/`RET`, stack args | `1` |
| 03 | starloop | assembly | counter loop (spec §9.2 golden) | `**********` |
| 04 | fib | assembly | iterative vs. recursive, `ROT` | `55 55` |
| 05 | bubble | assembly | in-place sort, compare-consume trap | `11345` |
| 06 | calc | assembly | console I/O ports, dispatch ladder | `56` (input `7*8=`) |
| 07 | factorial | C profile | recursion, calling convention | `720` |
| 08 | gcd | C profile | `while`, `%`, loop update order | `good` |
| 09 | stringrev | C profile | pointers, `*p`, `p++`/`p--` | `edcba` |
| 10 | binsearch | C profile | arrays, `a[i]`, binary search | `ok` |
| 11 | guess | C profile | console game, branch-range fixup | `LHY` (input `397`) |

## Building

```powershell
# build everything (samples/01-hello … 11-guess)
pwsh samples/build-all.ps1          # builds src/ST16.App first
pwsh samples/build-all.ps1 -NoBuild # if the app is already built
```

Outputs (`.bin` + `.st16d`) land next to each source file.

## Running

```powershell
st16 vm samples/02-sq/sq.bin                  # no input
echo 7*8= | st16 vm samples/06-calc/calc.bin  # with input
```

`st16 cli <bin>` opens the interactive debugger for any sample —
`bp set 0xNN`, `step`, `dump stack`, `dump memory 0xADDR N` are the
useful commands while working through the discussion questions.
