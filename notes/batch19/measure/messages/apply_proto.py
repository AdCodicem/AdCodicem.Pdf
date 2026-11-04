"""Applies the #241 prototype's message edits to the worktree: each old text must occur exactly once."""
import sys

ROOT = sys.argv[1]

EDITS = {
    "src/AdCodicem.Pdf/Validation/Rules/FileHeaderOffsetRule.cs": [
        ('$"The PDF header starts {header} bytes into the file instead of at its first byte."',
         '$"The PDF header starts {CountText.Bytes(header)} into the file instead of at its first byte."'),
    ],
    "src/AdCodicem.Pdf/Validation/PageTreeWalk.cs": [
        ('Invariant($"{MediaBoxOf(place, what)} is an array of {other.Count} values, not a rectangle.")',
         'Invariant($"{MediaBoxOf(place, what)} is an array of {CountText.Of(other.Count, "value", "values")}, not a rectangle.")'),
    ],
    "src/AdCodicem.Pdf/IO/PdfLimitGuard.cs": [
        ('_ => string.Create(CultureInfo.InvariantCulture, $"{length:N0} bytes"),',
         '_ => CountText.Grouped(length, "byte", "bytes"),'),
    ],
    "src/AdCodicem.Pdf/IO/PdfFileReader.cs": [
        # Deferred /Length of a cross-reference stream.
        ('{held}; the chain took its data up to its endstream, after {deferred.Taken} bytes.");',
         '{held}; the chain took its data up to its endstream, after {CountText.Bytes(deferred.Taken)}.");'),
        ('message = string.Create(CultureInfo.InvariantCulture, $"The stream declared {length} bytes but ended after {deferred.Taken}.");',
         'message = string.Create(CultureInfo.InvariantCulture, $"The stream declared {CountText.Bytes(length)} but ended after {deferred.Taken}.");'),
        ('gives {length} bytes, which an endstream confirms; the chain, which could not read it, took the data up to an endstream {deferred.Taken} bytes in, and read no row past it.");',
         'gives {CountText.Bytes(length)}, which an endstream confirms; the chain, which could not read it, took the data up to an endstream {CountText.Bytes(deferred.Taken)} in, and read no row past it.");'),
        # The header's offset.
        ('$"The PDF header starts {index} bytes into the file; offsets were shifted accordingly."',
         '$"The PDF header starts {CountText.Bytes(index)} into the file; offsets were shifted accordingly."'),
        # The chain's count of sections.
        ('$"The cross-reference chain has more than {_guard.Bound(PdfLimit.XRefSectionCount):N0} sections; the older ones were not read.")',
         '$"The cross-reference chain has more than {CountText.Grouped(_guard.Bound(PdfLimit.XRefSectionCount), "section", "sections")}; the older ones were not read.")'),
        ('$"The cross-reference chain has more than {_guard.Bound(PdfLimit.XRefSectionCount):N0} sections; the older ones were not read: the chain goes on',
         '$"The cross-reference chain has more than {CountText.Grouped(_guard.Bound(PdfLimit.XRefSectionCount), "section", "sections")}; the older ones were not read: the chain goes on'),
        # The section's relocation.
        ('names at offset {absolute} was found {candidate - absolute} bytes from there."),',
         'names at offset {absolute} was found {CountText.Distance(candidate - absolute, "it")}."),'),
        # The object's relocation.
        ('$"Object {id.Number} was found {nearby - offset} bytes from where the index said."),',
         '$"Object {id.Number} was found {CountText.Distance(nearby - offset, "where the index said")}."),'),
        # The object stream's fault, as the validator quotes it.
        ('CultureInfo.InvariantCulture, $"it decodes to {data.Length} bytes, fewer than its /First of {first}");',
         'CultureInfo.InvariantCulture, $"it decodes to {CountText.Bytes(data.Length)}, fewer than its /First of {first}");'),
        ('CultureInfo.InvariantCulture, $"its header lists {header.Count} of the {count} objects its /N declares, then ends"),',
         'CultureInfo.InvariantCulture, $"its header lists {header.Count} of the {CountText.Objects(count)} its /N declares, then ends"),'),
        ('CultureInfo.InvariantCulture, $"its header lists {header.Count} of the {count} objects its /N declares, then something else"),',
         'CultureInfo.InvariantCulture, $"its header lists {header.Count} of the {CountText.Objects(count)} its /N declares, then something else"),'),
        ('$"its header lists {header.Count} of the {count} objects its /N declares, then object {header.FaultNumber}',
         '$"its header lists {header.Count} of the {CountText.Objects(count)} its /N declares, then object {header.FaultNumber}'),
        # The object stream's fault, as the reader reports it.
        ('past the end of its decoded data, which is {data.Length:N0} bytes long"),',
         'past the end of its decoded data, which is {CountText.Grouped(data.Length, "byte", "bytes")} long"),'),
        ('$"has a header that ends after {header.Count:N0} of the {count:N0} objects its /N declares"),',
         '$"has a header that ends after {header.Count:N0} of the {CountText.Grouped(count, "object", "objects")} its /N declares"),'),
        ('an object number and an offset after {header.Count:N0} of the {count:N0} objects its /N declares"),',
         'an object number and an offset after {header.Count:N0} of the {CountText.Grouped(count, "object", "objects")} its /N declares"),'),
        ('{header.Why(data.Length)}, after {header.Count:N0} of the {count:N0} objects its /N declares"),',
         '{header.Why(data.Length)}, after {header.Count:N0} of the {CountText.Grouped(count, "object", "objects")} its /N declares"),'),
    ],
    "src/AdCodicem.Pdf/IO/PdfObjectParser.cs": [
        ('$"The stream declared {length} bytes, and no endstream follows them; the declared length is kept without a search,',
         '$"The stream declared {CountText.Bytes(length)}, and no endstream follows {(length == 1 ? "it" : "them")}; the declared length is kept without a search,'),
        ('the {kept} bytes {(search.NextObject is null ? "to the end of the file" : "up to the next object")} are taken as its data without a search,',
         'the {CountText.Bytes(kept)} {(search.NextObject is null ? "to the end of the file" : "up to the next object")} {(kept == 1 ? "is" : "are")} taken as its data without a search,'),
        ('$"The stream has no endstream: its object\'s endobj follows the {length} bytes it declared, which are taken as its data."),',
         '$"The stream has no endstream: its object\'s endobj follows the {CountText.Bytes(length)} it declared, which {(length == 1 ? "is" : "are")} taken as its data."),'),
        (': its object\'s endobj ends it, after {taken} bytes."),',
         ': its object\'s endobj ends it, after {CountText.Bytes(taken)}."),'),
        ('$"The stream declared {length} bytes, and no endstream follows them {Where(search.NextObject)}; the declared length is kept."),',
         '$"The stream declared {CountText.Bytes(length)}, and no endstream follows {(length == 1 ? "it" : "them")} {Where(search.NextObject)}; the declared length is kept."),'),
        ('; the {upToStop} bytes {(search.NextObject is null ? "to the end of the file" : "up to that object")} are taken as its data."),',
         '; the {CountText.Bytes(upToStop)} {(search.NextObject is null ? "to the end of the file" : "up to that object")} {(upToStop == 1 ? "is" : "are")} taken as its data."),'),
        ('? string.Create(CultureInfo.InvariantCulture, $"The stream declared {declared.Value} bytes but ended after {recovered}.")',
         '? string.Create(CultureInfo.InvariantCulture, $"The stream declared {CountText.Bytes(declared.Value)} but ended after {recovered}.")'),
        (': string.Create(CultureInfo.InvariantCulture, $"{Describe(declared)}; its data ends after {recovered} bytes.");',
         ': string.Create(CultureInfo.InvariantCulture, $"{Describe(declared)}; its data ends after {CountText.Bytes(recovered)}.");'),
        ('return string.Create(CultureInfo.InvariantCulture, $"The stream declared {declared.Value} bytes");',
         'return string.Create(CultureInfo.InvariantCulture, $"The stream declared {CountText.Bytes(declared.Value)}");'),
    ],
    "src/AdCodicem.Pdf/IO/Filters/PdfFilterPipeline.cs": [
        ('Invariant($"{subject} ends inside a run of {faults.UnitLength} bytes, of which it holds {faults.UnitHeld}; they were kept, and data may have been lost."),',
         'Invariant($"{subject} ends inside a run of {faults.UnitLength} bytes, of which it holds {faults.UnitHeld}; {(faults.UnitHeld == 1 ? "it was" : "they were")} kept, and data may have been lost."),'),
        ('$"The /Filter entry names more than {chain.EndIndex:N0} filters; decoding stopped after the first {chain.EndIndex:N0}."),',
         '$"The /Filter entry names more than {CountText.Grouped(chain.EndIndex, "filter", "filters")}; decoding stopped after the first{(chain.EndIndex == 1 ? string.Empty : " " + chain.EndIndex.ToString("N0", CultureInfo.InvariantCulture))}."),'),
        ('$"A Flate stream\'s checksum disagrees with the {decoded} bytes its data decoded to; all were kept, and some may be wrong."),',
         '$"A Flate stream\'s checksum disagrees with the {CountText.Bytes(decoded)} its data decoded to; {(decoded == 1 ? "it was kept, and may be wrong" : "all were kept, and some may be wrong")}."),'),
        ('$"A Flate stream is corrupt at byte {faultAt} of its {encoded}; the {decoded} bytes decoded before the fault was found were kept."),',
         '$"A Flate stream is corrupt at byte {faultAt} of its {encoded}; the {CountText.Bytes(decoded)} decoded before the fault was found {(decoded == 1 ? "was" : "were")} kept."),'),
    ],
}

for path, edits in EDITS.items():
    full = f"{ROOT}/{path}"
    text = open(full, encoding="utf-8").read()
    for old, new in edits:
        count = text.count(old)
        if count != 1:
            sys.exit(f"{path}: {count} occurrences of {old[:80]!r}")
        text = text.replace(old, new)
    open(full, "w", encoding="utf-8").write(text)
    print(f"{path}: {len(edits)} edits")
