**Batch 19's sixth adversarial review found that the chain's search near an offset also takes a section's word inside stream data.** The comment of 2026-11-06 brought this search under this issue for a line that starts inside a string an earlier line opened. Stream data is a second context the search does not see, and a wider one.

## How the search takes it

When a `/Prev` or an `/XRefStm` names an offset its section does not start at, short of it or past it, `PdfFileReader.TryRelocateXRefSection` tries each place near the offset, nearest first. `IsTokenOnItsLine` lexes the place's line alone, from its start, so it does not see stream data that an earlier line opened. In stream data whose bytes hold the text as written, it therefore takes:

- the word `xref`, wherever its line, lexed alone, reads it as a token, its middle included: `The xref tables …` as well as a line that starts with it;
- the header of an object whose dictionary gives `/Type /XRef`, such as an example the data quotes.

Stream data holds the text as written under no filter, as an embedded file that holds prose does, and also under a filter that keeps it byte for byte: a `/FlateDecode` stream written with a stored block (zlib level 0, which .NET's `CompressionLevel.NoCompression` writes), or the literal runs of `/RunLengthDecode`. Ordinary deflate output scatters the word, and the search then reads the file as main does.

No load lexes an embedded file's data. An object stream's members are lexed by a load, as an object's body is, and stay out of this case. But a place in such data declares itself a section and, being the nearest place, it ends the search. It cannot be read, so the reader:

- reports it as `xref.section-unreadable`, and the validator charges the file with an Error `xref.section-malformed`;
- ends the chain there, leaving unread the real section farther off, within the 512 bytes searched;
- rebuilds the index, and serves what the rebuild finds: an object the update freed, or, behind an `/XRefStm`, the revision the older table's row gives.

Where the real section lies within the 512 bytes searched, this is a regression against main, which dropped such a place and read the real section. It came with the batch's rule that the nearest place that declares itself a section ends the search even when it cannot be read, so that an older section farther off does not stand in silence for the lost one. Where no real section lies within the 512 bytes searched, the place is still taken. The rebuild and the objects served are then the same as on main, but the report is `xref.section-unreadable` and `Error xref.section-malformed`, where main gave `xref.section-missing` and `Error xref.section-not-found`.

Its reach is narrow. It needs an offset that misses its section, short of it or past it, as in a file that gained bytes or lost them. It needs stream data whose bytes hold the word or such a header as written. And that text must lie within the 512 bytes searched, nearer the offset than any real section.

Both reproducers below were run on `main` at `21a0298` and on batch 19's branch at `65306d4`. The tests that hold them, named under *Where it stands*, pass on the branch at `012e13d`. Offsets are counted from `%PDF`, and lines end with LF.

## Reproducer 1: a `/Prev` short of its table, an object the update freed served

The file is hand-built, with three revisions:

- **The oldest table**, at 175, indexes the catalog, an empty page tree, object 3 `(three)` and object 6 `(six, freed by the update)`.
- **The update** follows 1,200 bytes of comment. It writes object 4 at 1578, `<< /Type /EmbeddedFile /Length 146 >>`, uncompressed, with the data shown below. The `xref` on the data's second line starts at 1657. The update's table, at 1795, indexes object 4, frees object 6 (`0000000000 00001 f`) and gives `/Prev 175`.
- **The newest revision** writes object 5 `(five)`, then its table at 1959, which `startxref` names. Its trailer gives `/Prev 1659`: 2 bytes past the word in the data, and 136 bytes short of the update's table.

Object 4's data:

```
Notes on the file format.
xref tables list offsets; see section 7.5.4.
End of notes.
%xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

**Main** opens the file without a rebuild, and object 6 is null. The validator's one finding is `Warning xref.section-shifted at offset 1795`.

```
Repair xref.offset-adjusted at 1795: The cross-reference section /Prev names at offset 1659 was found 136 bytes from there.
```

**Batch 19's branch** takes the word for the update's table found moved. The chain ends there, and only the oldest table indexes the catalog, so the index is rebuilt as the document opens. Object 6 is then served as `(six, freed by the update)`.

```
Repair xref.offset-adjusted at 1657: The cross-reference section /Prev names at offset 1659 was found 2 bytes before it.
Warning xref.section-unreadable at 1657: The cross-reference table /Prev names at offset 1659, found at offset 1657, is there but cannot be read: it holds the keyword tables at offset 1662, where a subsection or the trailer should start. …
Repair xref.rebuilt: The cross-reference index was rebuilt by scanning the file.
```

The validator adds `Error xref.section-malformed at offset 1657: The cross-reference table at offset 1657 cannot be read: it holds the keyword tables at offset 1662, where a subsection or the trailer should start.`, and a `Warning xref.section-shifted` at the same offset.

Variants of the file:

- **The word in the middle of its line** (`The xref tables …`) gives the same result.
- **The data of reproducer 2**, a quoted header, in place of this data gives the same result too. The header, at 1640, with `/Prev 1642`, is taken for a stream that "cannot be read: its dictionary is not followed by stream data", and object 6 is again served.
- **`text` in the word's place**: the branch reads the file as main does. It finds the update's table 136 bytes after the place named, object 6 is null, and nothing is rebuilt.

## Reproducer 2: an `/XRefStm` short of its stream, the older revision served

The file is hand-built as PDF 1.5, with two revisions:

- **The oldest table**, at 137, indexes the catalog, an empty page tree and object 3 `(three old)`.
- **The update** follows 1,200 bytes of comment:
  - object 3 again, at 1480, as `(three new)`;
  - object 4, at 1507, an uncompressed `/EmbeddedFile` of `/Length 179`, with the data shown below; its `12 0 obj` starts at 1569;
  - cross-reference stream 7, at 1757, with `/W [1 4 2] /Index [3 2]`, indexing objects 3 and 4;
  - a classic table, at 1865, holding only row 0. Its trailer is `<< /Size 8 /Root 1 0 R /Prev 137 /XRefStm 1571 >>`: 2 bytes past the header in the data, and 186 bytes short of stream 7.

Object 4's data:

```
Example:
12 0 obj
<< /Type /XRef /Size 13 /W [1 2 1] >>
endobj
 tables list offsets; see section 7.5.4.
End of notes.
%xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

**Main** opens the file without a rebuild, and serves object 3 as `(three new)`:

```
Repair xref.offset-adjusted at 1757: The cross-reference section /XRefStm names at offset 1571 was found 186 bytes from there.
```

**Batch 19's branch** takes the header in the data for stream 7 found moved. The oldest table indexes the catalog, so nothing sets a rebuild off at opening. The oldest table's row for object 3 stands, and object 3, read first, is `(three old)`:

```
Repair xref.offset-adjusted at 1569: The cross-reference section /XRefStm names at offset 1571 was found 2 bytes before it.
Warning xref.section-unreadable at 1569: The cross-reference stream /XRefStm names at offset 1571, found at offset 1569, is there but cannot be read: its dictionary is not followed by stream data. …
```

Reading object 4, which only stream 7 indexes, sets a rebuild off, and so does a validation. The rebuild reports `xref.rebuilt`, then `object.redefined` for object 3. After it, the same document serves object 3 as `(three new)`. The validator reports `Error xref.section-malformed at offset 1569: The cross-reference stream at offset 1569 cannot be read: its dictionary is not followed by stream data.`

With `text` in the header's place, the branch reads the file as main does: it finds stream 7 136 bytes after the place named, and object 3 is `(three new)`. The word `xref` in this data is taken too, with the same Error and an `Error xref.hybrid-stream-wrong`. But a table taken in the stream's place refuses every number to the older table's rows, so the index is rebuilt as the document opens, and object 3 is `(three new)` whatever is read first. Under an `/XRefStm`, the older revision is served only through a header.

## Beyond the two reproducers

Measured on the branch at `012e13d` and on `main` at `21a0298`, on files of three revisions built like reproducer 1, whose update frees object 6, with the same prose:

- **An offset past its section.** The embedded file is written by the newest revision, after the update's table, and the newest `/Prev` names 2 bytes past its `xref`, 199 bytes past the update's table. The branch takes the word 2 bytes before the place named, reports `xref.section-unreadable`, rebuilds the index and serves object 6. Main finds the update's table 199 bytes before the place named, and object 6 is null.
- **A stored deflate block.** Object 4 is `/FlateDecode` data written at `CompressionLevel.NoCompression`. The branch takes the word as in reproducer 1 and serves object 6. Main finds the update's table 140 bytes after the place named, and object 6 is null. The same prose at `CompressionLevel.Optimal` holds no `xref` bytes, and the branch reads that file as main does.
- **No real section within reach.** The update's table lies 836 bytes past the place named. The branch takes the word, with `xref.section-unreadable` and `Error xref.section-malformed`. Main reports `xref.section-missing` and `Error xref.section-not-found`. Both rebuild the index, and both serve object 6.

## Where it stands

- **The docs.** The threat model's Known gaps entry for this issue now names stream data beside the string an earlier line opened. So do:
  - `IsTokenOnItsLine`'s summary and remarks;
  - the comment in `TryRelocateXRefSection` where a place that cannot be read is taken;
  - `docs/website/docs/concepts/lazy-reading.md`;
  - the rows the reference pages on diagnostics and validation rules give for this search.
- **The tests.** Two tests in `CrossReferenceChainTests` pin the behavior as it stands and cite this issue:
  - `The_word_xref_or_a_cross_reference_stream_s_header_in_stream_data_short_of_where_a_prev_names_is_taken_for_the_section_found_moved` holds reproducer 1, its mid-line and header variants, and its control;
  - `A_cross_reference_stream_s_header_in_stream_data_short_of_where_an_xrefstm_names_is_taken_for_the_stream_found_moved` holds reproducer 2 and its control.

  Their files come from `NearbyFiles.UpdatePastAWordInStreamData` and `NearbyFiles.HybridPastAWordInStreamData`.
- **What paying this issue pays.** Paying this issue pays this case: telling stream data apart, which the rebuild's scan needs as well, closes it for this search too, once it is asked of every place the search takes and not only by the line test. The next comment gives a place in stream data that reads, which the line test never sees. The two tests then flip. Object 6 is null and object 3 is `(three new)`, with no `xref.section-unreadable`, no Error and no rebuild.

Edited on 2026-11-07: the case needs an offset that misses its section, short of it or past it, not only one that falls short; the data taken is stream data whose bytes hold the word or header as written (no filter, a Flate stream written with a stored block, RunLength's literal runs), not only uncompressed text; where no real section lies within the 512 bytes searched, the place is still taken, the rebuild and the objects served being the same as on main but the report `xref.section-unreadable` and `Error xref.section-malformed` where main gave `xref.section-missing` and `Error xref.section-not-found`; "No load ever lexes stream data" became "No load lexes an embedded file's data", an object stream's members being lexed by a load, as an object's body is, and out of this case. Also: "anywhere on its line" became "wherever its line, lexed alone, reads it as a token"; a new section gives the three measured shapes; the reproducers' tests are said to pass at `012e13d`; and the closing says the check must be asked of every place the search takes.

---
_Generated by [Claude Code](https://claude.ai/code)_
