# 06 — calc.st16

**Difficulty:** intermediate · **Language:** ST16 assembly · **Build:** `st16 as calc.st16`

## Objective

A tiny two-operand calculator that reads the console: one digit, an
operator (`+ - * /`), one digit, `=`. Prints the result (0–99) without a
leading zero.

## Expected console output

| Input | Output |
|-------|--------|
| `7*8=` | `56` |
| `3+4=` | `7` |
| `9-2=` | `7` |
| `7/2=` | `3` |

## What to look at

- Console I/O goes through the ports: `LOAD 0xF001` reads one input byte
  (`0xFFFF` when the input is exhausted); `STORE 0xF000` writes one byte.
- The operator dispatch is a ladder of `EQ` + `JZ` — each `JZ` consumes
  the comparison result, and every arm **reloads** `op` from RAM.
- The tens-digit print keeps the digit *character* (`'0' + d`) on the
  stack, checks it against zero with `JZ no_tens`, and prints only when
  nonzero.

## Discussion questions

1. What happens if you type `7*8` without the `=`?
2. Division uses the VM's truncated signed `DIV` — what does `7/2` print,
   and what would `-7/2` print if the parser accepted signs?
3. Extend the calculator to accept two-digit operands. What changes?
