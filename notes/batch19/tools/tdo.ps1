param(
    [Parameter(Mandatory = $true)][int]$Seconds,
    [Parameter(Mandatory = $true)][string]$Log,
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)
# Runs one command in the current directory under a timeout, its output in $Log; kills the process tree on timeout.
$env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
$p = Start-Process -FilePath $Exe -ArgumentList $Rest -NoNewWindow -PassThru -RedirectStandardOutput $Log -RedirectStandardError "$Log.err"
"pid=$($p.Id)"
if (-not $p.WaitForExit($Seconds * 1000)) {
    taskkill /PID $p.Id /T /F | Out-Null
    'TIMEOUT'
}
"exit=$($p.ExitCode)"
