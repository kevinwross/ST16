# 09 — stringrev.c

**Difficulty:** intermediate · **Language:** ST16 C profile · **Build:** `st16 cc stringrev.c`

## Objective

Reverse `"abcde"` in place with two pointers walking from the ends — the
full pointer story of the C profile: `char *p`, `*p`, `p++`, `p--`, and
pointer comparison `q < p`.

## Expected console output

```
edcba
```

## What to look at

- Every `*p` is a two-step `LOAD` sequence in the generated assembly:
  load the pointer word, then load through it.
- `char` loads are sign-extended (`PUSH 8; SWAP; SLL; PUSH 8; SWAP; SLR`)
  and masked with `AND 0x00FF` so pointer bytes stay in range.
- `q++` / `p--` are pointer arithmetic — one **word** per step, because
  the ST16 profile stores one character per word.
- Pointers compare with plain signed `LT` — they are addresses, and the
  string sits in the `0xE000` globals region.

## Discussion questions

1. Why does the first loop stop *after* the NUL, followed by `p--`,
   instead of stopping before it?
2. What would break if `s` were declared `char s[5]`?
3. Find the instruction count for the whole run and estimate how much of
   it is the two `while (*r)` print loop.
