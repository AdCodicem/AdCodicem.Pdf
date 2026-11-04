import sys, os, logging
import pikepdf

logging.basicConfig(level=logging.WARNING, format='LOG %(name)s %(levelname)s %(message)s')
for path in sys.argv[1:]:
    name = os.path.basename(path)
    with pikepdf.open(path) as pdf:
        o = pdf.get_object(3, 0)
        s = 'stream ' + o.read_bytes().decode('latin-1') if isinstance(o, pikepdf.Stream) else repr(o)
        problems = [str(p).replace(path, name) for p in pdf.check_pdf_syntax()]
        print(name, 'libqpdf', pikepdf.__libqpdf_version__, 'obj3=', s, '| check:', problems[:4])
