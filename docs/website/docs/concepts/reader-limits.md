---
title: Reader limits
sidebar_position: 3
description: How the reader keeps every valid PDF readable while treating every file as hostile, and why its guards are on by default.
---

# Reader limits

Every PDF that is valid under the specification can be read. That promise has to live alongside another:
a file arriving from outside your system is treated as hostile, and a few kilobytes of it must not be able
to make the reader hold gigabytes. The two meet in the **reader limits**: bounds on what a file may make the
reader hold, on by default, each of which you can raise. The [reference](../reference/reader-limits.md) lists them;
[Read or refuse a document that reaches a limit](../guides/handle-reader-limits.md) says what to do when one is
reached.

## Hostile input

A PDF arrives from outside your system, so the reader treats every value in it as an attempt. No
allocation is sized by a number read from the file without a checked bound, no recursion is unbounded,
and no loop exits on an offset that came from the file.

That claim is tested rather than asserted. Besides the hand-written cases — a cross-reference chain that
loops, a stream claiming two gigabytes, an object stream declaring a billion objects, containers nested
twenty thousand deep, fifty thousand streams each taking its length from the next, a few megabytes of
RunLength data that would decode to hundreds, predictor rows whose length overflows — a mutation campaign
runs against the test corpus: bit flips, corrupted digits, truncations, spliced bytes and broken keywords,
each input required to end either in a usable document or in a typed exception, inside a time and an
allocation budget. A few thousand mutated documents go through
the reader on every test run — every pull request, and every package before it is published — and a
nightly campaign runs twenty thousand mutations per seed document.

The first campaign found a real defect within a minute: a mutated invoice made the reader's index lookup
and its relocation search call each other until the stack ran out. Relocation is now bounded to three
counted attempts. That is the kind of failure this exists to catch — no hand-written test had thought of
it, and a file that kills the process is the worst outcome a document reader can have.

## Guards a valid file can reach

Some of those bounds are reachable by a sound document: a large-format scan decodes past the 256 MB a stream may
decode to by default, and a file saved incrementally a thousand times has a thousand cross-reference sections. A
bound like that is a **guard**. It is on by default, so an application that opens files it did not produce is
protected without configuring anything; it is reported under its own `limit.*` code when a document reaches it; and
its message names the option that lifts it, so that the document stays readable by whoever needs to read it.

When a guard is reached, the reader keeps what fits and warns rather than fails. The limit is the reader's, not a
fault of the file, and it replaces what the parser met where it stopped: you are not told a stream was truncated
when it was the reader that stopped reading it. The bounds live on the options rather than on a static setting, so
two documents opened side by side can be read under different ones, and a stream decodes under the limits of the
document it came from, however long after opening.

`PdfReaderLimits.Unbounded` takes every limit to the most the implementation can hold. It is for documents your own
application produced, never for uploads: under it, a file can make the reader hold whatever it asks for, up to those
maxima.

## Bounds only a damaged file reaches

Other bounds protect the reader itself — its stack, its rebuild of a damaged index — and are not reached by the
documents producers write: real documents nest a handful of levels, not 128, and producers write an object rather
than a reference to a reference. Those stay internal constants, since lifting them would let nothing more be read,
and each says where it is declared why no valid file reaches it.

A new bound is classified when it is added ([ADR 34](/project/adr/every-valid-pdf-is-readable-and-the-readers-guards-are)):
if a valid file can reach it, it becomes a limit, with its code and its test.

One kind stays an internal constant though a valid file reaches it: a bound on what the library repeats of what it
read, not on what it reads. A message quotes at most 127 bytes of a name, which PDF 2.0 lets be longer, and the whole
name stays in the document; there is nothing more to read by lifting it. Its declaration says so instead ([How a
message quotes the file](../reference/diagnostics.md#how-a-message-quotes-the-file)).

## Refusing instead

Some applications would rather refuse a document than read part of it, and `ThrowOnLimit` turns every limit into an
exception. Reading is lazy, so that exception can come from any operation that reads — opening the document,
resolving an object, decoding a stream, validating —, long after `Open` returned. That is the price of reading only
what is asked for: a limit is met where the data is, and the data is read when you ask for it.
