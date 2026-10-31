# 04 — fib.st16

**Difficulty:** intermediate · **Language:** ST16 assembly · **Build:** `st16 as fib.st16`

## Objective

Compute `fib(10)` two ways and print both results:

1. **iterative** — `a`, `b` and the counter live in RAM words;
2. **recursive** — pure stack, no RAM at all.

## Expected console output

```
55 55
```

## What to look at

- The iterative step `(a, b) ← (b, a + b)` must load `b` **before**
  overwriting `a` — a one-instruction ordering mistake turns it into
  `b ← 2b` and the result goes wild (`c2` instead of `55`).
- `STORE` takes (value = second, address = top): the `a = b` store is
  three instructions, not four.
- The recursive variant's `rec:` block is the heart of the program.
  After `CALL fib_r` returns `f(n−1)`, the original `n` must still be
  available to compute `f(n−2)` — follow the `SWAP`/`ROT` sequence and
  check the stack after every instruction in the debugger.
- Call depth reaches 10 frames; the VM's max depth is 28.

## Discussion questions

1. Why does the base case `n < 2 → RET` cover both `n = 0` and `n = 1`?
2. Count the instructions executed for `fib(10)` recursively (the VM
   shows the instruction count in `state`). How does that compare to the
   iterative version?
3. What is the largest `n` the recursive version can handle before the
   call stack (28 frames) overflows?
