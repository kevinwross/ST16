# 01 — hello.st16

**Difficulty:** beginner · **Language:** ST16 assembly · **Build:** `st16 as hello.st16`

## Objective

Print `Hello, ST16!` from a string stored in RAM, one character at a time,
using an index loop that stops at the NUL terminator.

## Expected console output

```
Hello, ST16!
```
(a newline is printed after the last character)

## What to look at

- The string lives at label `msg` **in RAM**, not in the code — the loop
  loads each byte with `LOAD` and prints it with `STORE 0xF000`.
- The index `i` is a RAM word, incremented with `LOAD / ADD / STORE`.
- The loop test loads the *character* (not the index) and uses `JZ` —
  remember that `JZ` consumes the condition, so the test and the body
  each load the character once.

## Discussion questions

1. What would happen if the string had no NUL terminator?
2. `STORE` takes (value = second, address = top). Re-check the print
   sequence against that rule.
3. Could the loop be written with `JNZ` instead? Where would the label go?
