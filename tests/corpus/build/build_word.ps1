#Requires -Version 7
<#
.SYNOPSIS
Builds the corpus documents that only Microsoft Word on Windows can produce.

.DESCRIPTION
Word's own PDF writer ("Save as PDF"), the Windows print driver ("Microsoft Print to PDF") and the PDF24
virtual printer, which hands the print job to Ghostscript's pdfwrite, are three different writers of the
same page, all common on office desktops, and none of them runs in the Linux container that
build_corpus.py uses (W01 in docs/corpus-contributions.md). This script lays out the corpus's French
invoice — the content of sources/invoice-fr.html, fictitious from end to end — as a native Word document,
and writes it through all three.

Run it deliberately, on Windows with Word and PDF24 Creator installed, and review the diff: like every
producer, these change their output from one version to the next. The versions it prints belong in these
files' entries in tests/corpus/manifest.json, marked "builtBy": "build_word.ps1": build_corpus.py cannot
regenerate the files, and leaves them and their entries alone.

The document carries no personal data: its author is set to "AdCodicem" explicitly, both printers' habit of
stamping the Windows account that printed is dealt with where each allows it (below), and any file that
still names that account — in its raw bytes or in a decompressed stream — is deleted.
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..' 'documents' 'invoice')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Blue = 0x64381F        # #1f3864 as a Word BGR colour
$Grey = 0x666666
$PrintDriver = 'Microsoft Print to PDF'
$Pdf24Printer = 'PDF24'
$Pdf24Profile = 'default/best'      # the profile PDF24's printer applies unless told otherwise
$Pdf24Home = 'C:\Program Files\PDF24'

function Set-Paragraphs {
    # Fills a range with one paragraph per line, then formats the first line as a caption.
    param($Range, [string[]] $Lines, [switch] $CaptionFirst, [switch] $BoldFirst)

    $Range.Text = $Lines -join "`r"
    $first = $Range.Paragraphs.Item(1).Range
    if ($CaptionFirst) {
        $first.Font.Size = 8
        $first.Font.Color = $Grey
        $first.Font.AllCaps = $true
        $first.Font.Spacing = 1
    }
    if ($BoldFirst) {
        $first.Font.Bold = $true
    }
}

function New-Table {
    param($Document, $Selection, [int] $Rows, [int] $Columns)

    $table = $Document.Tables.Add($Selection.Range, $Rows, $Columns)
    $table.Borders.Enable = $false
    $table.TopPadding = $table.BottomPadding = 2
    return $table
}

function Move-AfterTables {
    param($Selection)

    $Selection.EndKey(6) | Out-Null   # wdStory
}

function Remove-DriverAuthor {
    # The Microsoft print driver writes the display name of the Windows account that printed into /Author,
    # whatever the document says — a real person's name, in a file bound for a public repository — and it
    # offers no intermediate file where that could be set beforehand. Each /Author token is overwritten in
    # place by "(AdCodicem)", or "()" when the original is shorter, padded with whitespace to the same length:
    # PDF allows any whitespace between tokens, so not one offset moves and everything else stays the
    # driver's own bytes. The file is recorded as derived for that reason.
    param([string] $Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $latin1 = [System.Text.Encoding]::Latin1
    $text = $latin1.GetString($bytes)
    $tokens = [regex]::Matches($text, '/Author\s*(\((?:\\.|[^\\)])*\)|<[0-9A-Fa-f\s]*>)')
    foreach ($token in $tokens) {
        $value = $token.Groups[1]
        $neutral = if ($value.Length -ge '(AdCodicem)'.Length) { '(AdCodicem)' } else { '()' }
        $replacement = $latin1.GetBytes($neutral.PadRight($value.Length, ' '))
        [System.Array]::Copy($replacement, 0, $bytes, $value.Index, $replacement.Length)
    }
    [System.IO.File]::WriteAllBytes($Path, $bytes)
    return $tokens.Count
}

function Wait-ForFile {
    # A printer writes asynchronously: the file is ready once it exists and has stopped growing.
    param([string] $Path, [string] $Writer, [int] $Seconds = 60)

    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    $lastSize = -1
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path $Path) {
            $size = (Get-Item $Path).Length
            if ($size -gt 0 -and $size -eq $lastSize) { return }
            $lastSize = $size
        }
        Start-Sleep -Milliseconds 500
    }
    throw "$Writer wrote nothing complete to $Path within $Seconds seconds."
}

function Get-SearchableText {
    # The raw bytes plus every Flate stream decoded, as Latin-1: a name can sit in a compressed object
    # stream or in compressed XMP just as well as in plain /Info.
    param([byte[]] $Bytes)

    $latin1 = [System.Text.Encoding]::Latin1
    $raw = $latin1.GetString($Bytes)
    $parts = [System.Collections.Generic.List[string]]::new()
    $parts.Add($raw)
    foreach ($match in [regex]::Matches($raw, 'stream\r?\n')) {
        $start = $match.Index + $match.Length
        $end = $raw.IndexOf('endstream', $start, [StringComparison]::Ordinal)
        if ($end -lt 0) { continue }
        try {
            $encoded = [System.IO.MemoryStream]::new($Bytes, $start, $end - $start)
            $zlib = [System.IO.Compression.ZLibStream]::new($encoded, [System.IO.Compression.CompressionMode]::Decompress)
            $output = [System.IO.MemoryStream]::new()
            $zlib.CopyTo($output)
            $parts.Add($latin1.GetString($output.ToArray()))
        }
        catch {
            # Not Flate, or not decodable on its own (images, fonts in other filters): nothing to search.
        }
    }
    return $parts -join "`n"
}

$outputs = @{
    SaveAs      = Join-Path $OutputDirectory 'word-invoice-fr.pdf'
    PrintDriver = Join-Path $OutputDirectory 'word-print-driver-invoice-fr.pdf'
    Pdf24       = Join-Path $OutputDirectory 'pdf24-invoice-fr.pdf'
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
foreach ($path in $outputs.Values) {
    if (Test-Path $path) { Remove-Item $path }
}

$previousDefault = (Get-CimInstance Win32_Printer | Where-Object Default).Name
$work = Join-Path ([System.IO.Path]::GetTempPath()) "adcodicem-word-$PID"
New-Item -ItemType Directory -Force $work | Out-Null

$word = New-Object -ComObject Word.Application
try {
    $word.Visible = $false
    $word.DisplayAlerts = 0                     # wdAlertsNone
    $word.Options.PrintBackground = $false      # PrintOut returns once the job is spooled

    $doc = $word.Documents.Add()
    $setup = $doc.PageSetup
    $setup.PaperSize = 7                        # wdPaperA4
    $setup.TopMargin = $setup.BottomMargin = $word.MillimetersToPoints(18)
    $setup.LeftMargin = $setup.RightMargin = $word.MillimetersToPoints(16)

    $normal = $doc.Styles.Item(-1)              # wdStyleNormal
    $normal.Font.Name = 'Arial'
    $normal.Font.Size = 10
    $normal.ParagraphFormat.SpaceAfter = 0
    $normal.ParagraphFormat.LineSpacingRule = 0 # wdLineSpaceSingle

    $heading = $doc.Styles.Item(-2)             # wdStyleHeading1
    $heading.Font.Name = 'Arial'
    $heading.Font.Size = 13
    $heading.Font.Bold = $true
    $heading.Font.Color = 0x1A1A1A
    $heading.ParagraphFormat.SpaceBefore = 18
    $heading.ParagraphFormat.SpaceAfter = 8

    $sel = $word.Selection

    # Letterhead: the issuer on the left, the invoice reference on the right, ruled underneath.
    $letterhead = New-Table $doc $sel 1 2
    Set-Paragraphs $letterhead.Cell(1, 1).Range @('AD CODICEM', 'Édition logicielle · 12 rue de la Paix, 75002 Paris')
    $brand = $letterhead.Cell(1, 1).Range.Paragraphs.Item(1).Range
    $brand.Font.Size = 16
    $brand.Font.Bold = $true
    $brand.Font.Color = $Blue
    $tagline = $letterhead.Cell(1, 1).Range.Paragraphs.Item(2).Range
    $tagline.Font.Size = 8.5
    $tagline.Font.Color = $Grey
    Set-Paragraphs $letterhead.Cell(1, 2).Range -BoldFirst @(
        'Facture n° F-2026-0481', 'Émise le 12 février 2026', 'Échéance le 14 mars 2026',
        'TVA intracommunautaire FR40123456824')
    $letterhead.Cell(1, 2).Range.ParagraphFormat.Alignment = 2   # wdAlignParagraphRight
    $letterhead.Cell(1, 2).Range.Font.Size = 9
    $rule = $letterhead.Borders.Item(-3)                         # wdBorderBottom
    $rule.LineStyle = 1
    $rule.LineWidth = 18                                         # wdLineWidth225pt
    $rule.Color = $Blue
    Move-AfterTables $sel

    $sel.Style = $heading
    $sel.TypeText('Facture')
    $sel.TypeParagraph()
    $sel.Style = $normal

    # The two parties, boxed side by side.
    $parties = New-Table $doc $sel 1 2
    $parties.Borders.Enable = $true
    $parties.TopPadding = $parties.BottomPadding = 6
    Set-Paragraphs $parties.Cell(1, 1).Range -CaptionFirst @(
        'Émetteur', 'AdCodicem SAS', '12 rue de la Paix', '75002 Paris, France', 'SIREN 123 456 824')
    Set-Paragraphs $parties.Cell(1, 2).Range -CaptionFirst @(
        'Client', 'Étude Noailles & Associés', '48 boulevard Haussmann', '75009 Paris, France', 'SIREN 552 041 319')
    Move-AfterTables $sel
    $sel.TypeParagraph()

    # Line items, with a header row Word repeats on every page and tags as a table header.
    $items = @(
        @('Désignation', 'Quantité', 'Prix unitaire', 'TVA', 'Montant HT'),
        @('Licence annuelle — édition Entreprise', '3', '1 250,00 €', '20 %', '3 750,00 €'),
        @('Intégration et reprise de données', '6', '780,00 €', '20 %', '4 680,00 €'),
        @('Formation des utilisateurs (journée)', '2', '950,00 €', '20 %', '1 900,00 €'),
        @('Support prioritaire — 12 mois', '1', '2 400,00 €', '20 %', '2 400,00 €'),
        @('Frais de déplacement (refacturation)', '1', '312,50 €', '10 %', '312,50 €'))
    $lines = New-Table $doc $sel $items.Count 5
    for ($row = 0; $row -lt $items.Count; $row++) {
        for ($column = 0; $column -lt 5; $column++) {
            $cell = $lines.Cell($row + 1, $column + 1)
            $cell.Range.Text = $items[$row][$column]
            if ($column -gt 0) { $cell.Range.ParagraphFormat.Alignment = 2 }
        }
        $lines.Rows.Item($row + 1).Borders.Item(-3).LineStyle = 1
        $lines.Rows.Item($row + 1).Borders.Item(-3).Color = 0xE5E5E5
    }
    $header = $lines.Rows.Item(1)
    $header.HeadingFormat = $true
    $header.Shading.BackgroundPatternColor = $Blue
    $header.Range.Font.Color = 0xFFFFFF
    $header.Range.Font.Bold = $true
    $header.Range.Font.Size = 9
    $lines.AllowAutoFit = $false
    $lines.PreferredWidthType = 2                                # wdPreferredWidthPercent
    $lines.PreferredWidth = 100
    $widths = @(40, 12, 18, 10, 20)
    for ($column = 0; $column -lt $widths.Count; $column++) {
        $lines.Columns.Item($column + 1).PreferredWidthType = 2
        $lines.Columns.Item($column + 1).PreferredWidth = $widths[$column]
    }
    Move-AfterTables $sel
    $sel.TypeParagraph()

    # Totals, right-aligned under the lines.
    $totals = New-Table $doc $sel 4 2
    $sums = @(@('Total HT', '13 042,50 €'), @('TVA 20 %', '2 546,00 €'), @('TVA 10 %', '31,25 €'), @('Total TTC', '15 619,75 €'))
    for ($row = 0; $row -lt $sums.Count; $row++) {
        $totals.Cell($row + 1, 1).Range.Text = $sums[$row][0]
        $totals.Cell($row + 1, 2).Range.Text = $sums[$row][1]
        $totals.Cell($row + 1, 2).Range.ParagraphFormat.Alignment = 2
    }
    $totals.PreferredWidthType = 3                               # wdPreferredWidthPoints
    $totals.PreferredWidth = $word.MillimetersToPoints(70)
    $totals.Rows.Alignment = 2                                   # wdAlignRowRight
    $grand = $totals.Rows.Item(4)
    $grand.Range.Font.Bold = $true
    $grand.Borders.Item(-1).LineStyle = 1                        # wdBorderTop
    $grand.Borders.Item(-1).LineWidth = 12
    $grand.Borders.Item(-1).Color = $Blue
    Move-AfterTables $sel
    $sel.TypeParagraph()

    $sel.Font.Size = 8.5
    $sel.Font.Color = 0x444444
    $sel.TypeText("Règlement par virement sous 30 jours. Pénalités de retard : trois fois le taux d'intérêt légal. " +
        'Indemnité forfaitaire pour frais de recouvrement : 40 €. Escompte pour paiement anticipé : néant.')

    # Legal footer, with page numbering from Word fields.
    $footer = $doc.Sections.Item(1).Footers.Item(1).Range            # wdHeaderFooterPrimary
    $footer.Text = 'AdCodicem SAS au capital de 50 000 € · RCS Paris 123 456 824 · IBAN FR76 3000 4008 2800 0123 4567 890'
    $footer.Font.Size = 7.5
    $footer.Font.Color = $Grey
    $footer.ParagraphFormat.Borders.Item(-1).LineStyle = 1
    $footer.ParagraphFormat.Borders.Item(-1).Color = 0xD0D0D0
    $footer.InsertParagraphAfter()
    $pageLine = $footer.Paragraphs.Item($footer.Paragraphs.Count).Range
    $pageLine.Text = 'Page '
    $pageLine.Collapse(0)                                        # wdCollapseEnd
    $doc.Fields.Add($pageLine, 33) | Out-Null                    # wdFieldPage
    $pageLine = $footer.Paragraphs.Item($footer.Paragraphs.Count).Range
    $pageLine.MoveEnd(1, -1) | Out-Null
    $pageLine.Collapse(0)
    $pageLine.InsertAfter(' sur ')
    $pageLine.Collapse(0)
    $doc.Fields.Add($pageLine, 26) | Out-Null                    # wdFieldNumPages
    $footer.Paragraphs.Item($footer.Paragraphs.Count).Alignment = 2

    # Metadata stated rather than inherited from the Office account running the script.
    $properties = $doc.BuiltInDocumentProperties
    foreach ($pair in @(@('Title', 'Facture F-2026-0481'), @('Author', 'AdCodicem'), @('Subject', 'Facture'),
                        @('Keywords', ''), @('Company', 'AdCodicem'), @('Manager', ''), @('Comments', ''))) {
        $property = [System.__ComObject].InvokeMember('Item', [System.Reflection.BindingFlags]::GetProperty, $null, $properties, @($pair[0]))
        [System.__ComObject].InvokeMember('Value', [System.Reflection.BindingFlags]::SetProperty, $null, $property, @($pair[1])) | Out-Null
    }

    # Saved under the invoice's name, so the print job — and whatever the driver derives from it — carries
    # that name rather than "Document1".
    $doc.SaveAs2((Join-Path $work 'Facture F-2026-0481.docx'), 16)  # wdFormatDocumentDefault

    # 1. Word's own writer, with its defaults: tagged, heading bookmarks, document properties.
    $doc.ExportAsFixedFormat($outputs.SaveAs, 17, $false, 0, 0, 1, 1, 0, $true, $true, 1, $true, $true, $false)

    # 2. The Windows print driver: Word prints, the driver writes the PDF.
    $word.ActivePrinter = $PrintDriver
    $missing = [System.Type]::Missing
    $doc.PrintOut($false, $false, 0, $outputs.PrintDriver, $missing, $missing, 0, 1, $missing, 0, $true, $true)
    Wait-ForFile $outputs.PrintDriver 'The print driver'
    $replaced = Remove-DriverAuthor $outputs.PrintDriver

    # 3. PDF24: its printer driver (PScript5) turns the job into PostScript, which PDF24 hands to
    #    Ghostscript's pdfwrite with the printer's profile. The job is printed to a file instead of PDF24's
    #    pipe and converted by PDF24's own tool with that same profile — the same driver, profile and
    #    Ghostscript, without the interactive assistant a printed job opens. Ghostscript copies the
    #    PostScript's %%For comment, the Windows account that printed, into /Author and the XMP, so that
    #    comment is set to AdCodicem in the PostScript first: the PDF itself is Ghostscript's, byte for byte.
    $postscript = Join-Path $work 'pdf24-job.ps'
    $word.ActivePrinter = $Pdf24Printer
    $doc.PrintOut($false, $false, 0, $postscript, $missing, $missing, 0, 1, $missing, 0, $true, $true)
    Wait-ForFile $postscript 'The PDF24 driver'

    $latin1 = [System.Text.Encoding]::Latin1
    $job = ([regex] '(?m)^%%For:[^\r\n]*').Replace($latin1.GetString([System.IO.File]::ReadAllBytes($postscript)), '%%For: AdCodicem')
    [System.IO.File]::WriteAllBytes($postscript, $latin1.GetBytes($job))

    $convert = Start-Process (Join-Path $Pdf24Home 'pdf24-DocTool.exe') -PassThru -ArgumentList @(
        '-applyProfile', '-profile', $Pdf24Profile, '-noProgress', '-outputFile', "`"$($outputs.Pdf24)`"", "`"$postscript`"")
    if (-not $convert.WaitForExit(120000)) { $convert.Kill(); throw 'PDF24 did not finish converting within two minutes.' }
    Wait-ForFile $outputs.Pdf24 'PDF24'

    $versions = [ordered]@{
        word        = "Microsoft Word $($word.Version) build $($word.Build)"
        windows     = & {
            $os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
            "Windows 11 $($os.DisplayVersion) (build $($os.CurrentBuild).$($os.UBR))"
        }
        pdf24       = "PDF24 Creator $((Get-Item (Join-Path $Pdf24Home 'pdf24.exe')).VersionInfo.ProductVersion)"
        ghostscript = "Ghostscript $((Get-Item (Join-Path $Pdf24Home 'gs\bin\gswin64c.exe')).VersionInfo.ProductVersion)"
    }
    $doc.Close(0)                                                # wdDoNotSaveChanges
}
finally {
    $word.Quit(0)
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
    if ($previousDefault -and (Get-CimInstance Win32_Printer | Where-Object Default).Name -ne $previousDefault) {
        Get-CimInstance Win32_Printer -Filter "Name='$previousDefault'" | Invoke-CimMethod -MethodName SetDefaultPrinter | Out-Null
    }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

# A producer may stamp metadata the document never asked for: the print driver, for one, writes the display
# name of the Windows account that printed. The repository is public, so a file naming anyone is deleted
# rather than left where it could be committed.
$accountNames = @($env:USERNAME)
try { $accountNames += (Get-LocalUser -Name $env:USERNAME -ErrorAction Stop).FullName } catch { }
$accountNames = @($accountNames | Where-Object { $_ })
foreach ($path in $outputs.Values) {
    $latin1 = Get-SearchableText ([System.IO.File]::ReadAllBytes($path))
    $authors = [regex]::Matches($latin1, '/Author\s*(\((?:\\.|[^\\)])*\)|<[0-9A-Fa-f\s]*>)') | ForEach-Object { $_.Groups[1].Value }
    $foreign = @($authors | Where-Object { $_ -ne '(AdCodicem)' })
    $leaked = @($accountNames | Where-Object {
        $latin1.Contains($_, [StringComparison]::OrdinalIgnoreCase) -or
        $latin1.Contains([System.Text.Encoding]::Latin1.GetString([System.Text.Encoding]::BigEndianUnicode.GetBytes($_)))
    })
    if ($foreign.Count -gt 0 -or $leaked.Count -gt 0) {
        Remove-Item $path
        throw "$(Split-Path $path -Leaf) named someone ($(@($foreign + $leaked) -join ', ')) and was deleted."
    }
}

$versions.GetEnumerator() | ForEach-Object { '{0}: {1}' -f $_.Key, $_.Value }
"print driver: $replaced /Author token(s) overwritten in place"
foreach ($path in $outputs.Values) {
    '{0}  {1:N0} bytes' -f (Resolve-Path $path -Relative), (Get-Item $path).Length
}
