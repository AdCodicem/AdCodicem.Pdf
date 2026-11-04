import sys, io, contextlib, logging, warnings
warnings.filterwarnings('ignore')
path = sys.argv[1]


def show(v):
    return repr(v)[:120]


# pypdf
try:
    import pypdf
    logging.getLogger('pypdf').setLevel(logging.ERROR)
    buf = io.StringIO()
    with contextlib.redirect_stderr(buf):
        r = pypdf.PdfReader(path, strict=False)
        o = r.get_object(3)
        try:
            if hasattr(o, 'get_data'):
                o = 'stream ' + o.get_data().decode('latin-1')
        except Exception as e:
            o = 'stream?' + str(e)
        t = r.pages[0].extract_text().strip()
    print('pypdf', pypdf.__version__, ': obj3=' + show(o) + ' | text=[' + t + ']')
except Exception as e:
    print('pypdf: ERROR', type(e).__name__, e)

# pikepdf
try:
    import pikepdf
    with pikepdf.open(path) as pdf:
        w = [str(x) for x in pdf.get_warnings()]
        o = pdf.get_object(3, 0)
        if isinstance(o, pikepdf.Stream):
            s = 'stream ' + o.read_bytes().decode('latin-1')
        else:
            s = show(o)
        print('pikepdf', pikepdf.__version__, '(libqpdf', pikepdf.__libqpdf_version__ + '): obj3=' + s + ' | warnings=' + str(w)[:400])
except Exception as e:
    print('pikepdf: ERROR', type(e).__name__, str(e)[:300])

# PyMuPDF
try:
    import pymupdf
    pymupdf.TOOLS.mupdf_warnings(reset=True)
    doc = pymupdf.open(path)
    try:
        s = doc.xref_object(3, compressed=True)
        if doc.xref_is_stream(3):
            s += ' stream ' + doc.xref_stream(3).decode('latin-1')
    except Exception as e:
        s = 'ERROR ' + str(e)
    t = doc[0].get_text().strip() if doc.page_count else ''
    print('MuPDF', pymupdf.mupdf_version, ': obj3=' + s.replace('\n', ' ') + ' | text=[' + t + '] repaired=' + str(doc.is_repaired)
          + ' | warnings=' + pymupdf.TOOLS.mupdf_warnings().replace('\n', '; ')[:300])
except Exception as e:
    print('MuPDF: ERROR', type(e).__name__, e)
