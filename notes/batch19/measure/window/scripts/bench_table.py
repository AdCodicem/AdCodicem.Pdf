import csv, os, sys
# Gathers the benchmark CSVs of each run into one table: mean (ms) and allocated, per case and run.
root = sys.argv[1]
runs = sys.argv[2:]
rows = {}
for run in runs:
    folder = os.path.join(root, 'bench-' + run, 'results')
    for name in sorted(os.listdir(folder)):
        if not name.endswith('-report.csv'):
            continue
        kind = name.split('.')[3].split('-')[0]
        with open(os.path.join(folder, name), newline='') as handle:
            for record in csv.DictReader(handle):
                case = kind + ' ' + (record.get('Shape') or record.get('Tail') or '')
                rows.setdefault(case, {})[run] = (record['Mean'], record['StdDev'], record['Allocated'])
print('| Case | ' + ' | '.join(runs) + ' |')
print('|---|' + '---|' * len(runs))
for case, values in rows.items():
    cells = []
    for run in runs:
        mean, deviation, allocated = values.get(run, ('-', '-', '-'))
        cells.append(f'{mean} ± {deviation}, {allocated}')
    print('| ' + case + ' | ' + ' | '.join(cells) + ' |')
