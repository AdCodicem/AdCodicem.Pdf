**The chain's search near an offset also takes a section in stream data that reads, whole or in part.** The comment above covers a place there that cannot be read. A place that reads is another case: an uncompressed attached PDF's own table, say, or a cross-reference stream the data quotes. `PdfFileReader.TryRelocateXRefSection` accepts such a place as the section found moved before any line is lexed, and asks `IsTokenOnItsLine` only of a place that cannot be read. Main takes it the same way: this predates the batch, and the branch does no worse.

The chain then goes on through that section's trailer:

- where the trailer gives no `/Prev`, the chain ends there: the update's rows are lost, and the objects the update frees are served;
- where the trailer quotes a `/Prev`, the chain follows it.

## A reproducer

The file is hand-built, with three revisions. Offsets are counted from `%PDF`, and lines end with LF.

- **The oldest table**, at 175, indexes the catalog, an empty page tree, object 3 `(three)` and object 6 `(six, freed by the update)`.
- **The update** follows 1,200 bytes of comment. It writes object 4 at 1580, `<< /Type /EmbeddedFile /Length 160 >>`, uncompressed, whose data is the attached PDF shown below. Its `xref` starts at 1661. The update's table, at 1811, indexes object 4, frees object 6 and gives `/Prev 175`.
- **The newest revision** writes object 5 `(five)`, then its table at 1975, which `startxref` names. Its trailer gives `/Prev 1663`: 2 bytes past the attached file's `xref`, and 148 bytes short of the update's table.

Object 4's data:

```
%PDF-1.3
%attached original
xref
0 1
0000000000 65535 f 
trailer
<< /Size 1 >>
startxref
28
%%EOF
%zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz
```

**Main at `21a0298` and the branch at `012e13d` alike** take the attached table at 1661, read whole, for the update's table found moved. Its trailer gives no `/Prev`, so the chain ends there, and the update's table goes unread. Only the oldest table, which the chain no longer reaches, indexes the catalog, so the index is rebuilt as the document opens. Object 6 is then served as `(six, freed by the update)`, and object 4 as the embedded file.

```
Repair xref.offset-adjusted at 1661: The cross-reference section /Prev names at offset 1663 was found 2 bytes before it.
Repair xref.rebuilt: The cross-reference index was rebuilt by scanning the file.
```

Main words the first message "was found -2 bytes from there". The place reads, so nothing reports it unreadable. The validator calls the section only shifted (`Warning xref.section-shifted`, "starts 2 bytes before it, at offset 1661"), and nothing says the update's table was lost. It also gives a `Warning file.size-wrong`, and an `Error file.root-invalid` whose last sentence, "No object of the file is a catalog", is false here, as #260 describes for a rebuild run as the document opens.

With `text` in the word's place, both read the file as it was meant: they find the update's table 148 bytes after the place named, object 6 is null, and nothing is rebuilt.

Two variants, the same on both:

- **The attached table quotes a `/Prev`.** With the data `%PDF-1.3`, then the same `xref` and row 0, and a trailer `<< /Size 1 /Prev 12 >>`, both take the attached table 2 bytes before the place named, then follow its `/Prev 12` to the oldest table, found 163 bytes after it. Nothing is rebuilt. Object 6 is served from the oldest table's row, and object 4, which only the update's table indexes, reads null. The validator gives two `Warning xref.section-shifted` and a `Warning file.size-wrong`.
- **A cross-reference stream the data quotes, read in part.** With `12 0 obj`, `<< /Type /XRef /Size 13 /W [1 2 1] /Prev 9 /Length 3 >>` and three bytes of stream data, the `/Prev` 2 bytes past the header, both take the stream and follow its `/Prev 9` to the oldest table. The branch reports `xref.section-unreadable` ("read only in part: it holds 0 rows where its /Index and /Size declare 13") and rebuilds the index, which finds object 4. Main reported only the two offsets it adjusted, rebuilt nothing, and served object 4 as null. Object 6 is served on both.

## What closing this issue needs

Closing this issue's search case therefore needs the stream-data check asked of every place the search takes, one that reads included, not only of the line test. Teaching `IsTokenOnItsLine` alone about stream data closes the case of the comment above and leaves this one open.

- `CrossReferenceChainTests.A_section_in_stream_data_that_reads_short_of_where_a_prev_names_is_taken_and_the_chain_goes_on_through_its_trailer` pins the three shapes as they stand: the attached table, the attached table quoting a `/Prev`, and the quoted stream read in part. It cites this issue. It flips when the check reaches a place that reads: the update's table is then read, as in the control above.
- `CrossReferenceChainTests.The_word_xref_or_a_cross_reference_stream_s_header_in_a_string_short_of_where_a_prev_names_is_taken_for_the_section_found_moved_only_where_an_earlier_line_opened_the_string` pins the string context of the comment of 2026-11-06, in an object's body. The word `xref` at the start of a line or in its middle, and a `/Type /XRef` header, each on a line inside a string an earlier line opened, are taken: the chain ends there, and object 6 is served through a rebuild. The same string written on one line is the control, read as it was meant. Main read the three taken rows correctly; that regression is the one the comment of 2026-11-06 records.
- The threat model's Known gaps entry for this issue now names a place that reads, and says that telling stream data, and strings an earlier line opened, apart, asked of every place the search takes, one that reads included, closes it.

---
_Generated by [Claude Code](https://claude.ai/code)_
