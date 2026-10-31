# 11 — guess.c

**Difficulty:** advanced · **Language:** ST16 C profile · **Build:** `st16 cc guess.c`

## Objective

The high/low game: read digits from the console until the player guesses
`7`, answering `L` (low), `H` (high), or `Y` (correct). Input `397`
answers `L`, `H`, `Y`.

## Expected console output

| Input | Output |
|-------|--------|
| `397` | `LHY` |
| `1357` | `LLLY` |
| `9` | `H` |

## What to look at

- This program's `while` loop body lowers to **more than 127 words** —
  wider than the `JZ`/`JNZ` reach. `st16cc` rewrites out-of-range
  branches as a short inverted branch plus an absolute `JMP` relay
  (`JZ far` → `JNZ skip; JMP far; skip:`). Look for the `fix_N` labels
  in the generated assembly — this sample is the reason that pass
  exists.
- Console input: `getchar()` lowers to `LOAD 0xF001`, which returns
  `0xFFFF` when the input runs out; the loop also stops on `q` and on
  end-of-input, so the program always terminates.
- The three-way branch (`<`, `>`, `==`) is an `EQ`/`LT` ladder with a
  reload of the guess value at each arm.

## Discussion questions

1. Disassemble and count the words between the loop's `JZ` and its
   target — confirm the relay was inserted.
2. What does the program print for input `77`?
3. Why is `0xFFFF` used for end-of-input instead of a NUL byte?
