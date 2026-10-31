# 03 — starloop.st16

**Difficulty:** beginner · **Language:** ST16 assembly · **Build:** `st16 as starloop.st16`

## Objective

The spec §9.2 example, verbatim: print ten asterisks with a counter
decremented by `SUB 1` in a loop that exits when the counter reaches zero.

This is the program used for the assembler's 25-word golden-image test —
`dotnet test` checks its exact word encoding.

## Expected console output

```
**********
```

## What to look at

- The counter starts at `10` and the loop body is exactly four words:
  print, `PUSH 1`, `SUB`, `DUP`-free counter update via `JZ`.
- `JZ done` jumps **when the counter is zero**; the branch offset is
  measured from the *next* instruction and must stay within ±127 words.
- Compare the disassembly (`st16 ds starloop.bin -d starloop.st16d`)
  against the 25-word table in spec §9.2.

## Discussion questions

1. Why is the counter `DUP`ed (or how is it kept) across the loop?
2. What is the farthest a `JZ` in this program could reach, and how far
   is it from that limit?
3. Change the counter to `200`. What breaks, and how would you fix it?
