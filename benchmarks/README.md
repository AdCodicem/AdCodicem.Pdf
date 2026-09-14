# Benchmarks

Performance benchmarks using [BenchmarkDotNet](https://benchmarkdotnet.org/).

## Convention

- One project per area worth measuring: `benchmarks/{{ProjectName}}.Benchmarks/`.
- Reference `BenchmarkDotNet` from the benchmark project only — never from
  `src/`.
- Benchmark classes live next to what they measure conceptually (e.g.
  `SerializationBenchmarks.cs`), not in one giant file.

## Running

Benchmarks are **not** run automatically in CI on every push — noisy,
shared runners don't produce trustworthy numbers, and a full BenchmarkDotNet
run is slow. Run them deliberately:

```bash
dotnet run --configuration Release --project benchmarks/{{ProjectName}}.Benchmarks
```

Or trigger the `Benchmarks` GitHub Actions workflow manually (Actions tab →
Benchmarks → Run workflow) to get results as a downloadable artifact —
useful before a release, or when investigating a suspected regression.
