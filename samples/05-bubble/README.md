# 05 — bubble.st16

**Difficulty:** intermediate · **Language:** ST16 assembly · **Build:** `st16 as bubble.st16`

## Objective

Bubble-sort the 5-element RAM array `[3, 1, 4, 1, 5]` in place and print
the sorted digits.

## Expected console output

```
11345
```

## What to look at

- The outer pass counter **counts down** (`3, 2, 1, 0`) and the loop
  exits with `JNZ sorted` — `JZ`/`JNZ` jump on **zero/nonzero**, so the
  exit test is easy to invert by one instruction and the loop silently
  never (or always) runs.
- The compare-and-swap is the subtle part: `GT` **consumes both
  operands**, so the values must be reloaded from RAM before the two
  swap stores.
- `STORE` again takes (value = second, address = top) — the swap writes
  `arr[i] = v2` and `arr[i+1] = v1` without any extra `SWAP`.

## Discussion questions

1. Why does pass `p` only need to compare up to index `p`?
2. How many `LOAD`/`STORE` pairs does one swap cost, and could a scratch
   word reduce that?
3. Change the array to `[5, 4, 3, 2, 1]` (worst case) and compare the
   instruction count with the given array.
