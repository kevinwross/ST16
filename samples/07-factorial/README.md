# 07 — factorial.c

**Difficulty:** intermediate · **Language:** ST16 C profile · **Build:** `st16 cc factorial.c`

## Objective

Recursive `factorial(6) = 720`, printed with a small two-digit digit
printer — the first C sample: functions, a loop-free recursion, and the
`st16cc` calling convention in action.

## Expected console output

```
720
```

## What to look at

- `st16 cc` produces `factorial.s`, `factorial.bin`, and `factorial.st16d`.
  Disassemble the binary and find where `factorial` calls itself — the
  caller stores the argument into the callee's frame slot **before** the
  `CALL` (the caller-stores-args convention).
- The frame prologue is `SP ← [SP] − frameSize`, where `frameSize`
  includes a 16-word spill budget even for this tiny function.
- The digit printer is the same two-digit idiom as in `04-fib` —
  compare the C-lowered version with the hand-written one.

## Discussion questions

1. What is the largest `n` for which `factorial(n)` fits in one 16-bit
   word?
2. Open the generated assembly: how does `st16cc` lower the `if`/base
   case of the recursion?
3. Why does the call chain for `factorial(6)` need 7 frames at most?
