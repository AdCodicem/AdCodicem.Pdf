# 35. Unsafe code, where a measurement asks for it

Date: 2026-09-26

## Status

Accepted on 2026-09-26, at the maintainer's word: nothing obliges the library to stay within managed code,
and unsafe code is acceptable where performance or memory requires it. No code uses it yet;
`Directory.Build.props` keeps `AllowUnsafeBlocks` false until the first use.

## Context

The core is managed, dependency-free, and compatible with Native AOT and trimming ([9](0009-a-dependency-free-core-plus-satellites.md),
invariant 1), and `Directory.Build.props` sets `AllowUnsafeBlocks` to false for every project. Nothing
records whether that is a principle or a default, and two needs are already in sight:

- **Decoding past about 2 GB.** A decoded stream is returned as `ReadOnlyMemory<byte>`, whose length is an
  `int`; ADR 34 leaves streams larger than `Array.MaxLength` to a decode that yields its output a piece at a
  time (T28, M13). Native memory is one way to hold such data without the garbage collector's large-object
  heap.
- **Hot loops.** Invariant 3 forbids allocation in parsing, layout and writing; bounds checks and pinned
  buffers are the next cost there, once measured.

Unsafe code does not threaten AOT or trimming: pointers, `NativeMemory` and `Unsafe` all compile ahead of
time. What it threatens is memory safety, in a library whose every input is hostile (invariant 4).

## Decision

We will allow unsafe code — pointers, `fixed`, `stackalloc` into pointers, `NativeMemory`, and the unchecked
members of `Unsafe` and `MemoryMarshal` — in the core and the satellites where a benchmark or a memory
measurement shows the need, under four conditions:

- **Measured first.** The change that introduces it ships the benchmark or measurement that justifies it,
  beside the managed version it replaces (invariant 9).
- **Contained.** It lives in a few named types, behind a safe API; `AllowUnsafeBlocks` is enabled per
  project on first use, never for the whole solution.
- **Reasoned.** Every unsafe block states, in a comment, the invariant that keeps it in bounds — and no value
  read from a file reaches an unchecked access without a check the comment names.
- **Tested as hostile input is.** The mutation fuzzing and the properties reach every unsafe site, and the
  site has tests at its bounds.

It adds no native dependency: P/Invoke to a native library stays out of the core (invariant 1).

## Consequences

- Where it is measured to matter, the library can hold data outside the managed heap and read it without
  bounds checks — the path to streams larger than 2 GB, and to the throughput M13 will budget.
- A memory-safety defect becomes possible where one was not: an out-of-bounds read in unsafe code is a
  vulnerability, not an exception. The four conditions are what keeps that risk where it pays.
- Reviews of such changes are heavier, and the fuzzing campaign has one more thing to reach.
- **Rejected** — forbidding unsafe code: it would close the simplest path past 2 GB, and a measured hot loop
  would have to stay slower than it needs to be.
- **Rejected** — allowing it freely: in a library whose inputs are all hostile, unsafe code without a measured
  reason is risk bought for nothing.
- **What would reopen it**: a memory-safety defect in unsafe code, which would argue for narrowing the rule;
  or managed facilities that meet the same needs, which would make it unnecessary.
