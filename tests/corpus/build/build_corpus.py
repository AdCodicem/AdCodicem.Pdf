#!/usr/bin/env python3
"""Builds the test corpus from real PDF producers.

The corpus is committed, not generated at test time: producer output changes with producer version, and a
suite that shifts underneath you is worse than no suite. Run this script deliberately, review the diff,
and record the producer versions it prints in the manifest.

Producers used:
  * Chromium (Skia PDF backend)  - cross-reference streams, object streams, Type0 subsets
  * LibreOffice                  - classic cross-reference tables, a different font pipeline, PDF/A export
  * ReportLab                    - a third writer, plus AcroForms and image-only pages
  * qpdf via pikepdf             - encryption, linearisation, object-stream rewrites, attachments

Damaged variants are derived by byte-level surgery on a valid document, so each one isolates exactly one
defect.
"""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCES = ROOT / "sources"
DOCUMENTS = ROOT / "documents"
VENDOR = ROOT / "vendor.json"
MANIFEST = ROOT / "manifest.json"

CHROMIUM_CANDIDATES = [
    "/opt/pw-browsers/chromium-1194/chrome-linux/chrome",
    "/usr/bin/chromium",
    "/usr/bin/chromium-browser",
    "/usr/bin/google-chrome",
]


def chromium_path() -> str | None:
    for candidate in CHROMIUM_CANDIDATES:
        if Path(candidate).exists():
            return candidate
    return shutil.which("chromium") or shutil.which("google-chrome")


def run(command: list[str], **kwargs) -> subprocess.CompletedProcess:
    return subprocess.run(command, check=True, capture_output=True, text=True, timeout=600, **kwargs)


def producer_versions() -> dict[str, str]:
    versions: dict[str, str] = {}

    chrome = chromium_path()
    if chrome:
        versions["chromium"] = run([chrome, "--version"]).stdout.strip()
    if shutil.which("soffice"):
        versions["libreoffice"] = run(["soffice", "--version"]).stdout.strip().splitlines()[0]

    import pikepdf
    import reportlab

    versions["reportlab"] = f"ReportLab {reportlab.Version}"
    versions["qpdf"] = f"qpdf {pikepdf.__libqpdf_version__} via pikepdf {pikepdf.__version__}"
    return versions


# --------------------------------------------------------------------------------------- generators


def build_with_chromium(source: Path, target: Path) -> None:
    chrome = chromium_path()
    if not chrome:
        raise RuntimeError("Chromium is required to build the corpus")

    target.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as profile:
        run([
            chrome,
            "--headless",
            "--disable-gpu",
            "--no-sandbox",
            "--no-pdf-header-footer",
            f"--user-data-dir={profile}",
            f"--print-to-pdf={target}",
            source.resolve().as_uri(),
        ])


def build_with_libreoffice(source: Path, target: Path, pdf_a: bool = False) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    convert_to = "pdf"
    if pdf_a:
        # SelectPdfVersion 2 is PDF/A-2b in the LibreOffice export filter.
        convert_to = 'pdf:writer_pdf_Export:{"SelectPdfVersion":{"type":"long","value":2}}'

    with tempfile.TemporaryDirectory() as outdir:
        profile = Path(outdir) / "profile"
        run([
            "soffice",
            "--headless",
            f"-env:UserInstallation={profile.as_uri()}",
            "--convert-to", convert_to,
            "--infilter=HTML (StarWriter)",
            "--outdir", outdir,
            str(source.resolve()),
        ])
        produced = next(Path(outdir).glob("*.pdf"))
        shutil.copyfile(produced, target)


def build_reportlab_invoice(target: Path) -> None:
    from reportlab.lib import colors
    from reportlab.lib.pagesizes import A4
    from reportlab.lib.styles import getSampleStyleSheet
    from reportlab.lib.units import mm
    from reportlab.platypus import Paragraph, SimpleDocTemplate, Spacer, Table, TableStyle

    target.parent.mkdir(parents=True, exist_ok=True)
    styles = getSampleStyleSheet()
    document = SimpleDocTemplate(
        str(target), pagesize=A4, title="Facture F-2026-0482", author="AdCodicem",
        leftMargin=18 * mm, rightMargin=18 * mm, topMargin=18 * mm, bottomMargin=18 * mm)

    rows = [["Designation", "Qty", "Unit price", "Amount"]]
    items = [
        ("Licence annuelle - edition Entreprise", 3, 1250.0),
        ("Integration et reprise de donnees", 6, 780.0),
        ("Formation des utilisateurs (journee)", 2, 950.0),
        ("Support prioritaire - 12 mois", 1, 2400.0),
    ]
    total = 0.0
    for label, quantity, price in items:
        amount = quantity * price
        total += amount
        rows.append([label, str(quantity), f"{price:,.2f} EUR", f"{amount:,.2f} EUR"])
    rows.append(["", "", "Total HT", f"{total:,.2f} EUR"])

    table = Table(rows, colWidths=[85 * mm, 15 * mm, 35 * mm, 35 * mm])
    table.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#1f3864")),
        ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
        ("ALIGN", (1, 0), (-1, -1), "RIGHT"),
        ("GRID", (0, 0), (-1, -2), 0.25, colors.HexColor("#cccccc")),
        ("LINEABOVE", (2, -1), (-1, -1), 1, colors.HexColor("#1f3864")),
        ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"),
        ("FONTNAME", (2, -1), (-1, -1), "Helvetica-Bold"),
    ]))

    document.build([
        Paragraph("AD CODICEM", styles["Title"]),
        Paragraph("Facture n° F-2026-0482 — émise le 12 février 2026", styles["Normal"]),
        Spacer(1, 8 * mm),
        table,
        Spacer(1, 6 * mm),
        Paragraph("Règlement par virement sous 30 jours.", styles["Normal"]),
    ])


def build_reportlab_form(target: Path) -> None:
    from reportlab.lib.colors import HexColor, black, white
    from reportlab.lib.pagesizes import A4
    from reportlab.lib.units import mm
    from reportlab.pdfgen import canvas

    target.parent.mkdir(parents=True, exist_ok=True)
    page = canvas.Canvas(str(target), pagesize=A4)
    page.setTitle("Bulletin de souscription")
    width, height = A4

    page.setFont("Helvetica-Bold", 16)
    page.drawString(20 * mm, height - 25 * mm, "Bulletin de souscription")
    page.setFont("Helvetica", 10)
    page.drawString(20 * mm, height - 33 * mm, "Merci de completer les champs ci-dessous.")

    form = page.acroForm
    fields = [("nom", "Nom"), ("prenom", "Prenom"), ("societe", "Societe"), ("courriel", "Courriel")]
    y = height - 50 * mm
    for name, label in fields:
        page.setFont("Helvetica", 10)
        page.drawString(20 * mm, y + 2 * mm, f"{label} :")
        form.textfield(
            name=name, tooltip=label, x=55 * mm, y=y - 1 * mm, width=100 * mm, height=8 * mm,
            borderColor=HexColor("#666666"), fillColor=white, textColor=black, forceBorder=True)
        y -= 14 * mm

    form.checkbox(name="newsletter", tooltip="Newsletter", x=55 * mm, y=y, size=8 * mm,
                  borderColor=HexColor("#666666"), fillColor=white, checked=False)
    page.drawString(66 * mm, y + 2 * mm, "Je souhaite recevoir la lettre d'information")
    page.showPage()
    page.save()


def build_reportlab_scan(target: Path) -> None:
    """A page that is nothing but an image, as a scanner produces."""
    from PIL import Image, ImageDraw
    from reportlab.lib.pagesizes import A4
    from reportlab.lib.utils import ImageReader
    from reportlab.pdfgen import canvas

    target.parent.mkdir(parents=True, exist_ok=True)
    image = Image.new("RGB", (1240, 1754), "white")
    draw = ImageDraw.Draw(image)
    draw.rectangle([60, 60, 1180, 200], outline="black", width=3)
    draw.text((90, 110), "RECU DE DEPOT - service courrier", fill="black")
    for index in range(14):
        y = 260 + index * 42
        draw.line([90, y, 1150 - (index % 5) * 60, y], fill=(40, 40, 40), width=2)
    draw.line([90, 1500, 500, 1500], fill="black", width=2)
    draw.text((90, 1510), "Signature", fill="black")

    # A little noise, so the page looks scanned rather than drawn.
    for index in range(400):
        x = (index * 977) % 1240
        y = (index * 613) % 1754
        draw.point((x, y), fill=(120, 120, 120))

    with tempfile.TemporaryDirectory() as tmp:
        jpeg = Path(tmp) / "scan.jpg"
        image.save(jpeg, "JPEG", quality=72)
        page = canvas.Canvas(str(target), pagesize=A4)
        page.setTitle("Recu de depot (numerise)")
        page.drawImage(ImageReader(str(jpeg)), 0, 0, width=A4[0], height=A4[1])
        page.showPage()
        page.save()


def build_reportlab_stress(target: Path, pages: int) -> None:
    from reportlab.lib.pagesizes import A4
    from reportlab.lib.units import mm
    from reportlab.pdfgen import canvas

    target.parent.mkdir(parents=True, exist_ok=True)
    page = canvas.Canvas(str(target), pagesize=A4)
    page.setTitle(f"Journal des operations - {pages} pages")

    for number in range(1, pages + 1):
        page.setFont("Helvetica-Bold", 12)
        page.drawString(20 * mm, 277 * mm, f"Journal des operations - page {number}")
        page.setFont("Helvetica", 9)
        for line in range(48):
            reference = (number * 97 + line * 13) % 100000
            page.drawString(
                20 * mm, (265 - line * 5) * mm,
                f"{number:05d}-{line:02d}  reference {reference:06d}  montant {reference / 100:10.2f} EUR  "
                f"statut {'valide' if line % 3 else 'en attente'}")
        page.showPage()

    page.save()


# ------------------------------------------------------------------------------------- derivations


def derive_encrypted(source: Path, target: Path, password: str) -> None:
    import pikepdf

    target.parent.mkdir(parents=True, exist_ok=True)

    with pikepdf.open(source) as pdf:
        pdf.save(target, encryption=pikepdf.Encryption(owner=password, user=password, R=6))


def derive_linearized(source: Path, target: Path) -> None:
    import pikepdf

    target.parent.mkdir(parents=True, exist_ok=True)

    with pikepdf.open(source) as pdf:
        pdf.save(target, linearize=True)


def derive_object_streams(source: Path, target: Path) -> None:
    import pikepdf

    target.parent.mkdir(parents=True, exist_ok=True)

    with pikepdf.open(source) as pdf:
        pdf.save(target, object_stream_mode=pikepdf.ObjectStreamMode.generate)


def derive_with_attachment(source: Path, target: Path, xml: bytes) -> None:
    import pikepdf

    target.parent.mkdir(parents=True, exist_ok=True)

    with pikepdf.open(source) as pdf:
        pdf.attachments["factur-x.xml"] = pikepdf.AttachedFileSpec(
            pdf, xml, filename="factur-x.xml", mime_type="text/xml",
            description="Factur-X invoice data")
        pdf.save(target)


# ------------------------------------------------------------------------------------------ damage


def damage_remove_xref(data: bytes) -> bytes:
    """Cut everything from the cross-reference table onwards: the index is simply gone."""
    index = data.rfind(b"\nxref")
    if index < 0:
        index = data.rfind(b"startxref")
    return data[:index] + b"\n%%EOF\n"


def damage_shift_offsets(data: bytes) -> bytes:
    """Insert bytes after the header so every recorded offset is wrong by the same amount."""
    header_end = data.index(b"\n", data.index(b"%PDF-")) + 1
    return data[:header_end] + b"% inserted by a careless post-processor\n" + data[header_end:]


def damage_truncate(data: bytes) -> bytes:
    """Lose the tail, as an interrupted download does."""
    return data[: int(len(data) * 0.82)]


def damage_junk_prefix(data: bytes) -> bytes:
    """Prepend junk, as a mail gateway or a broken transfer does."""
    return b"HTTP/1.1 200 OK\r\nContent-Type: application/pdf\r\n\r\n" + data


def damage_lying_length(data: bytes) -> bytes:
    """Make the first stream declare a length it does not have."""
    match = re.search(rb"/Length (\d+)", data)
    if not match:
        return data
    declared = match.group(1)
    replacement = str(max(1, int(declared) // 3)).encode().rjust(len(declared), b"0")
    return data[: match.start(1)] + replacement + data[match.end(1) :]


# name -> (transformation, diagnostic codes the reader must report, whether the index must be rebuilt)
DAMAGES = {
    "no-xref": (damage_remove_xref, ["xref.rebuilt"], True),
    # Shifting every offset also moves the cross-reference section startxref points at, so the whole
    # index has to be rebuilt rather than each object relocated.
    "shifted-offsets": (damage_shift_offsets, ["xref.rebuilt"], True),
    "truncated-tail": (damage_truncate, ["xref.rebuilt"], True),
    "junk-prefix": (damage_junk_prefix, ["xref.offset-adjusted"], False),
    # A wrong /Length is only noticed when the stream is actually read: that is the lazy reader working
    # as designed, so the acceptance test reads every object before checking the diagnostics.
    "lying-length": (damage_lying_length, ["stream.length-invalid"], False),
}


# ---------------------------------------------------------------------------------------- manifest


def page_count(path: Path, password: str = "") -> int:
    """Counted by an independent tool: the manifest must not be produced by the code it checks."""
    import pikepdf

    with pikepdf.open(path, password=password) as pdf:
        return len(pdf.pages)


def referee_page_count(path: Path) -> int | None:
    """What an independent tool can still recover from a damaged file, or None if it recovers nothing."""
    import pikepdf

    try:
        return page_count(path)
    except Exception:
        return None


def main() -> int:
    if DOCUMENTS.exists():
        shutil.rmtree(DOCUMENTS)
    DOCUMENTS.mkdir(parents=True)

    versions = producer_versions()
    entries: list[dict] = []

    def record(path: Path, **fields) -> None:
        entry = {"file": str(path.relative_to(ROOT)).replace(os.sep, "/")}
        entry.update(fields)
        entries.append(entry)

    invoice = DOCUMENTS / "invoice" / "chromium-invoice-fr.pdf"
    build_with_chromium(SOURCES / "invoice-fr.html", invoice)
    record(invoice, title="French invoice, VAT breakdown, accented text", useCase="invoice",
           producer=versions.get("chromium", "chromium"), origin="generated", licence="MIT (our own source)",
           features=["xref-stream", "object-streams", "type0-subset"],
           expect={"pages": page_count(invoice), "clean": True, "indexRebuilt": False, "requiredDiagnostics": [],
                   "textContains": ["Facture", "Total TTC", "TVA"]})

    lo_invoice = DOCUMENTS / "invoice" / "libreoffice-invoice-fr.pdf"
    build_with_libreoffice(SOURCES / "invoice-fr.html", lo_invoice)
    record(lo_invoice, title="Same invoice through a different producer", useCase="invoice",
           producer=versions.get("libreoffice", "libreoffice"), origin="generated",
           licence="MIT (our own source)", features=["xref-table", "type1-and-truetype"],
           expect={"pages": page_count(lo_invoice), "clean": True, "indexRebuilt": False, "requiredDiagnostics": [],
                   "textContains": ["Facture"]})

    rl_invoice = DOCUMENTS / "invoice" / "reportlab-invoice.pdf"
    build_reportlab_invoice(rl_invoice)
    record(rl_invoice, title="Invoice from a third writer", useCase="invoice",
           producer=versions["reportlab"], origin="generated", licence="MIT (our own source)",
           features=["xref-table", "standard-14-fonts"],
           expect={"pages": page_count(rl_invoice), "clean": True, "indexRebuilt": False, "requiredDiagnostics": [],
                   "textContains": ["AD CODICEM"]})

    report = DOCUMENTS / "report" / "chromium-report-fr.pdf"
    build_with_chromium(SOURCES / "report-fr.html", report)
    record(report, title="Multi-page audit report: contents, tables, two-column annex", useCase="report",
           producer=versions.get("chromium", "chromium"), origin="generated", licence="MIT (our own source)",
           features=["xref-stream", "object-streams", "internal-links", "repeated-table-headers",
                     "two-column-text"],
           expect={"pages": page_count(report), "clean": True, "indexRebuilt": False, "requiredDiagnostics": [],
                   "textContains": ["Sommaire", "Tableau des mesures", "Annexe"]})

    lo_report = DOCUMENTS / "report" / "libreoffice-report-fr.pdf"
    build_with_libreoffice(SOURCES / "report-fr.html", lo_report)
    record(lo_report, title="Same report through a different producer", useCase="report",
           producer=versions.get("libreoffice", "libreoffice"), origin="generated",
           licence="MIT (our own source)", features=["xref-table"],
           expect={"pages": page_count(lo_report), "clean": True, "indexRebuilt": False, "requiredDiagnostics": [],
                   "textContains": ["Sommaire"]})

    contract = DOCUMENTS / "contract" / "chromium-contract-fr.pdf"
    build_with_chromium(SOURCES / "contract-fr.html", contract)
    record(contract, title="Service contract, justified body text", useCase="contract",
           producer=versions.get("chromium", "chromium"), origin="generated", licence="MIT (our own source)",
           features=["xref-stream", "object-streams"],
           expect={"pages": page_count(contract), "clean": True, "indexRebuilt": False, "requiredDiagnostics": [],
                   "textContains": ["Article 1", "Droit applicable"]})

    attached = DOCUMENTS / "invoice" / "qpdf-invoice-with-facturx-xml.pdf"
    derive_with_attachment(invoice, attached, b"""<?xml version="1.0" encoding="UTF-8"?>
<rsm:CrossIndustryInvoice xmlns:rsm="urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100">
  <rsm:ExchangedDocument><ram:ID>F-2026-0481</ram:ID></rsm:ExchangedDocument>
</rsm:CrossIndustryInvoice>
""")
    record(attached, title="Invoice carrying an embedded Factur-X XML attachment", useCase="invoice",
           producer=versions["qpdf"], origin="derived", licence="MIT (derived from our own document)",
           features=["embedded-file", "xref-stream"],
           expect={"pages": page_count(attached), "clean": True, "indexRebuilt": False, "requiredDiagnostics": [],
                   "attachments": ["factur-x.xml"]})

    form = DOCUMENTS / "form" / "reportlab-subscription-form.pdf"
    build_reportlab_form(form)
    record(form, title="Interactive subscription form with text fields and a checkbox", useCase="form",
           producer=versions["reportlab"], origin="generated", licence="MIT (our own source)",
           features=["acroform", "widget-annotations"],
           expect={"pages": page_count(form), "clean": True, "indexRebuilt": False, "requiredDiagnostics": [],
                   "formFields": ["nom", "prenom", "societe", "courriel", "newsletter"]})

    scan = DOCUMENTS / "scan" / "reportlab-scanned-receipt.pdf"
    build_reportlab_scan(scan)
    record(scan, title="Image-only page, as a scanner produces", useCase="scan",
           producer=versions["reportlab"], origin="generated", licence="MIT (our own source)",
           features=["dct-image", "no-text"],
           expect={"pages": page_count(scan), "clean": True, "indexRebuilt": False, "requiredDiagnostics": [],
                   "hasExtractableText": False})

    archival = DOCUMENTS / "archival" / "libreoffice-report-pdfa2b.pdf"
    build_with_libreoffice(SOURCES / "report-fr.html", archival, pdf_a=True)
    record(archival, title="Archival export of the report, claiming PDF/A-2b", useCase="archival",
           producer=versions.get("libreoffice", "libreoffice"), origin="generated",
           licence="MIT (our own source)", features=["pdf-a-2b", "xmp-metadata", "output-intent",
                                                     "embedded-fonts"],
           expect={"pages": page_count(archival), "clean": True, "indexRebuilt": False, "requiredDiagnostics": [],
                   "claimsConformance": "PDF/A-2b"})

    linearized = DOCUMENTS / "archival" / "qpdf-linearized-report.pdf"
    derive_linearized(report, linearized)
    record(linearized, title="Linearised report, laid out for fast web viewing", useCase="report",
           producer=versions["qpdf"], origin="derived", licence="MIT (derived from our own document)",
           features=["linearized", "hint-stream"],
           expect={"pages": page_count(linearized), "clean": True, "indexRebuilt": False, "requiredDiagnostics": []})

    objstm = DOCUMENTS / "report" / "qpdf-objstm-report.pdf"
    derive_object_streams(report, objstm)
    record(objstm, title="Report rewritten with every object packed into object streams", useCase="report",
           producer=versions["qpdf"], origin="derived", licence="MIT (derived from our own document)",
           features=["object-streams", "xref-stream"],
           expect={"pages": page_count(objstm), "clean": True, "indexRebuilt": False, "requiredDiagnostics": []})

    encrypted = DOCUMENTS / "secured" / "qpdf-invoice-aes256.pdf"
    derive_encrypted(invoice, encrypted, "corpus")
    record(encrypted, title="Invoice encrypted with AES-256", useCase="invoice",
           producer=versions["qpdf"], origin="derived", licence="MIT (derived from our own document)",
           features=["encryption-aes256"],
           expect={"pages": page_count(encrypted, "corpus"), "encrypted": True, "password": "corpus",
                   "clean": True, "indexRebuilt": False, "requiredDiagnostics": []})

    stress_pages = 1000
    stress = DOCUMENTS / "stress" / f"reportlab-journal-{stress_pages}-pages.pdf"
    build_reportlab_stress(stress, stress_pages)
    record(stress, title=f"Operations journal of {stress_pages} pages", useCase="report",
           producer=versions["reportlab"], origin="generated", licence="MIT (our own source)",
           features=["many-pages", "shared-resources"],
           expect={"pages": stress_pages, "clean": True, "indexRebuilt": False, "requiredDiagnostics": []})

    original = invoice.read_bytes()
    for name, (damage, expected_codes, rebuild) in DAMAGES.items():
        damaged = DOCUMENTS / "damaged" / f"invoice-{name}.pdf"
        damaged.parent.mkdir(parents=True, exist_ok=True)
        damaged.write_bytes(damage(original))

        # What a damaged file still contains is established by an independent tool, never by our own
        # reader: an expectation derived from the code under test proves nothing.
        recovered = referee_page_count(damaged)

        record(damaged, title=f"Invoice damaged on purpose: {name.replace('-', ' ')}", useCase="invoice",
               producer=f"derived from {invoice.name}", origin="derived",
               licence="MIT (derived from our own document)", features=[f"damage-{name}"],
               expect={"pages": recovered, "clean": False, "indexRebuilt": rebuild,
                       "requiredDiagnostics": expected_codes})

    # Third-party documents are committed under vendor/ with their provenance, and are never touched by
    # this script: they cannot be regenerated, only attributed. See tests/corpus/NOTICE.
    vendored = json.loads(VENDOR.read_text(encoding="utf-8"))["documents"] if VENDOR.exists() else []
    entries.extend(vendored)

    MANIFEST.write_text(
        json.dumps({"producers": versions, "documents": entries}, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8")

    total = sum(path.stat().st_size for path in ROOT.rglob("*.pdf"))
    print(f"{len(entries)} documents ({len(vendored)} third-party), {total / 1024 / 1024:.1f} MB")
    for name, version in versions.items():
        print(f"  {name}: {version}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
