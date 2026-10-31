# 02 — sq.st16

**Difficulty:** beginner · **Language:** ST16 assembly · **Build:** `st16 as sq.st16`

## Objective

The spec §9.1 example: compute `sq(7) = 49` and print the result.
`49` is the ASCII code for `'1'`, so the console shows `1`.

## Expected console output

```
1
```

## What to look at

- `CALL sq` is **two words** in the image: `0x2000` followed by the
  function's address. The VM pushes `PC + 2` (past both words) before
  jumping.
- The function takes its argument from the top of the operand stack and
  leaves the result there — no RAM, no frame.
- Only one `STORE 0xF000` at the end prints the result; the stack must be
  empty after it (an extra `DROP` here is the classic off-by-one fault).

## Discussion questions

1. Why does the console show `1` and not `49`?
2. Trace the stack step by step through `sq`: what is on top after each
   instruction?
3. What would `sq(0)` print? What about `sq(255)`?
