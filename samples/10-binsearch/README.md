# 10 — binsearch.c

**Difficulty:** intermediate · **Language:** ST16 C profile · **Build:** `st16 cc binsearch.c`

## Objective

Binary search over `int a[8] = {1, 3, 5, 7, 9, 11, 13, 15}` with four
assertion checks, printing `ok` when they all pass.

## Expected console output

```
ok
```

## What to look at

- Array initializers with a brace list: `int a[8] = { ... }` lowers to a
  run of `.dw` data words in the globals region — disassemble and find
  them.
- `a[mid]` is `base + mid` address arithmetic followed by a `LOAD` — the
  same idiom as `*p` in `09-stringrev`, but with a computed offset.
- The loop maintains `lo <= hi` with signed comparisons; watch the
  `JZ`/`JNZ` targets in the generated assembly and verify each offset
  is within ±127 words.

## Discussion questions

1. How many comparisons does the search for `15` perform, versus `2`
   (not present)?
2. Where in the generated code is the `mid` computation, and does it
   guard against `(lo + hi)` overflowing 16 bits?
3. What changes if the array is not sorted?
