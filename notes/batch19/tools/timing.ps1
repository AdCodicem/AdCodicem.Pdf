param([string]$Out = 'C:\Users\clemm\AppData\Local\Temp\claude\7278f9d03177d71e\898783fc-049f-469a-ab79-dfd299ad558e\scratchpad\cloud\r9f')
# Runs the timing, budget, allocation and memory groups three times each, one run at a time, from the current worktree.
# usage: pwsh -File timing.ps1 [-Out <folder>] (default: the r9f folder).
$env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
$out = $Out
New-Item -ItemType Directory -Force $out | Out-Null
$groups = [ordered]@{
    'timing'  = @('--filter-class', '*ReaderTimingTests')
    'budget'  = @('--filter-method', '*budget*', '--filter-method', '*Budget*')
    'alloc'   = @('--filter-method', '*llocat*')
    'memory'  = @('--filter-class', '*HostileInputMemoryTests')
    'hostile' = @('--filter-class', '*.HostileInputTests')
}
$summary = Join-Path $out 'timing-summary.txt'
Set-Content $summary ''
for ($run = 1; $run -le 3; $run++) {
    foreach ($name in $groups.Keys) {
        $log = Join-Path $out "timing-$name-$run.log"
        $args = @('test', '--project', 'tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj', '-c', 'Release', '--no-build', '--') + $groups[$name]
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $p = Start-Process -FilePath dotnet -ArgumentList $args -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"
        if (-not $p.WaitForExit(1800 * 1000)) { taskkill /PID $p.Id /T /F | Out-Null; Add-Content $summary "$name run $run TIMEOUT" }
        $watch.Stop()
        $totals = (Select-String -Path $log -Pattern '^\s+(total|failed|succeeded|skipped|duration):' | ForEach-Object { $_.Line.Trim() }) -join ', '
        Add-Content $summary ("{0} run {1} exit={2} wall={3:n1}s {4}" -f $name, $run, $p.ExitCode, $watch.Elapsed.TotalSeconds, $totals)
    }
}
Add-Content $summary 'done'
