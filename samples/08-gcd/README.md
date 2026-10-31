# 08 — gcd.c

**Difficulty:** beginner · **Language:** ST16 C profile · **Build:** `st16 cc gcd.c`

## Objective

Euclid's algorithm in the C profile: `while (b != 0) { int t = b; b = a % b; a = t; }`,
plus a `puts`-style string print to finish with `good`.

## Expected console output

```
good
```

(the program asserts `gcd(48, 18) == 6` and prints `good` when it holds)

## What to look at

- `%` on `int` values lowers to the VM's signed `MOD` — check the
  generated assembly for the `MOD` word.
- The loop variable update `b = a % b; a = t;` shows the compiler
  handling the classic "update in the right order" problem: a temporary
  is only needed because the second assignment reads the *old* `b`.
- Compare the loop's `JNZ`/`JZ` against the hand-written loops in
  `03-starloop` and `05-bubble`.

## Discussion questions

1. What does `gcd(0, n)` return with this code?
2. Trace the `a % b` step in the generated assembly: which word holds the
   dividend and which the divisor?
3. Why is the loop condition `b != 0` (not `a != 0`)?
