[CmdletBinding()]
param(
    [string]$RemoteHost,
    [string]$RemoteDirectory = "/root/weremf",
    [string]$TmuxSession = "weremf",
    [string]$RemoteEnvFile = "/root/weremf.env",
    [ValidateRange(1, 65535)][int]$Port = 5000,
    [string]$ConfigPath,
    [switch]$DebugApi
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotenvPath = Join-Path $repoRoot ".env"
if (Test-Path -LiteralPath $dotenvPath -PathType Leaf) {
    foreach ($line in Get-Content -LiteralPath $dotenvPath) {
        if ($line -match '^\s*WEREMF_DEPLOY_HOST\s*=\s*(.*?)\s*$' -and [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable("WEREMF_DEPLOY_HOST", "Process"))) {
            $dotenvHost = $Matches[1].Trim().Trim('"').Trim("'")
            [Environment]::SetEnvironmentVariable("WEREMF_DEPLOY_HOST", $dotenvHost, "Process")
        }
    }
}
if ([string]::IsNullOrWhiteSpace($RemoteHost)) { $RemoteHost = [Environment]::GetEnvironmentVariable("WEREMF_DEPLOY_HOST", "Process") }
if ([string]::IsNullOrWhiteSpace($ConfigPath)) { $ConfigPath = Join-Path $repoRoot "WereMF/config.json" }
$ConfigPath = [IO.Path]::GetFullPath($ConfigPath)
if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { throw "找不到抽签配置：$ConfigPath" }
if ($RemoteDirectory -eq "/" -or $RemoteDirectory -notmatch '^/[A-Za-z0-9._/-]+$') { throw "RemoteDirectory 必须是安全的绝对路径，且不能是 /" }
if ($RemoteEnvFile -eq "/" -or $RemoteEnvFile -notmatch '^/[A-Za-z0-9._/-]+$') { throw "RemoteEnvFile 必须是安全的绝对路径，且不能是 /" }
if ($TmuxSession -notmatch '^[A-Za-z0-9_-]+$') { throw "TmuxSession 含有不安全字符" }
if ([string]::IsNullOrWhiteSpace($RemoteHost)) { throw "请通过 -RemoteHost、WEREMF_DEPLOY_HOST 或仓库根目录 .env 指定 SSH 目标" }
if ($RemoteHost -notmatch '^[A-Za-z0-9_.@:-]+$') { throw "RemoteHost 含有不安全字符" }
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$tempBase = [IO.Path]::GetTempPath()
$tempRoot = Join-Path $tempBase ("weremf-deploy-" + [Guid]::NewGuid().ToString("N"))
$serverOut = Join-Path $tempRoot "server"
$gameOut = Join-Path $tempRoot "game"
$bundle = Join-Path $tempRoot "bundle"
$archive = Join-Path $tempRoot "weremf-$stamp.tar.gz"
$remoteArchive = "/tmp/weremf-deploy-$stamp.tar.gz"
$remoteScript = "/tmp/weremf-deploy-$stamp.sh"
$remoteEnv = "/tmp/weremf-env-$stamp"
function Run([string]$File, [string[]]$Arguments) { & $File @Arguments; if ($LASTEXITCODE -ne 0) { throw "$File 退出码 $LASTEXITCODE" } }
try {
    New-Item -ItemType Directory -Path $serverOut, $gameOut, $bundle | Out-Null
    Run dotnet @("publish", (Join-Path $repoRoot "WereMF/WereMF.fsproj"), "-c", "Release", "-r", "linux-x64", "--self-contained", "true", "-o", $gameOut)
    Run dotnet @("publish", (Join-Path $repoRoot "WereMFServer/WereMFServer.csproj"), "-c", "Release", "-r", "linux-x64", "--self-contained", "true", "-o", $serverOut)
    Copy-Item -LiteralPath (Join-Path $serverOut "WereMFServer") -Destination $bundle
    foreach ($botNamesFile in @("bots_prefer.txt", "bots.txt")) {
        $publishedBotNames = Join-Path $serverOut $botNamesFile
        if (Test-Path -LiteralPath $publishedBotNames -PathType Leaf) { Copy-Item -LiteralPath $publishedBotNames -Destination $bundle }
    }
    if (Test-Path -LiteralPath (Join-Path $serverOut "wwwroot")) { Copy-Item -LiteralPath (Join-Path $serverOut "wwwroot") -Destination $bundle -Recurse }
    $gameBundle = Join-Path $bundle "game"; New-Item -ItemType Directory -Path $gameBundle | Out-Null
    Get-ChildItem -LiteralPath $gameOut | Copy-Item -Destination $gameBundle -Recurse
    Copy-Item -LiteralPath $ConfigPath -Destination (Join-Path $bundle "config.json")
    Run tar.exe @("-czf", $archive, "-C", $bundle, ".")
    $debugArg = if ($DebugApi) { " --debug-api" } else { "" }
    $remoteBody = @"
set -euo pipefail
remote_dir='$RemoteDirectory'; incoming='${RemoteDirectory}.incoming-$stamp'; backup='${RemoteDirectory}-backup-$stamp'; failed='${RemoteDirectory}.failed-$stamp'
archive='$remoteArchive'; session='$TmuxSession'; port='$Port'; env_upload='$remoteEnv'; env_file='$RemoteEnvFile'
command -v tmux >/dev/null; command -v curl >/dev/null
[ ! -e "`$incoming" ]; mkdir -p "`$incoming"; tar -xzf "`$archive" -C "`$incoming"
chmod +x "`$incoming/WereMFServer" "`$incoming/game/WereMF"; rm -f "`$archive"
if [ -f "`$env_upload" ]; then install -m 600 "`$env_upload" "`$env_file"; rm -f "`$env_upload"; fi
start_server() { local target="`$1" game_path="./game/WereMF"; [ -x "`$target/game/WereMF" ] || game_path="./WereMF"; tmux new-session -d -s "`$session" "cd '`$target'; set -a; if [ -f '`$env_file' ]; then . '`$env_file'; fi; set +a; exec ./WereMFServer --path `$game_path --config ./config.json --host 0.0.0.0 --port `$port$debugArg"; }
if tmux has-session -t "`$session" 2>/dev/null; then tmux kill-session -t "`$session"; fi
if [ -e "`$remote_dir" ]; then [ ! -e "`$backup" ]; mv "`$remote_dir" "`$backup"; fi
mv "`$incoming" "`$remote_dir"; start_server "`$remote_dir"
for _ in `$(seq 1 30); do if curl -fsS "http://127.0.0.1:`$port/api/health" >/dev/null 2>&1; then echo "Deployment healthy"; exit 0; fi; sleep 1; done
echo "Health check failed; rolling back" >&2
if tmux has-session -t "`$session" 2>/dev/null; then tmux kill-session -t "`$session"; fi
mv "`$remote_dir" "`$failed"
if [ -e "`$backup" ]; then mv "`$backup" "`$remote_dir"; start_server "`$remote_dir"; fi
exit 1
"@
    $localScript = Join-Path $tempRoot "deploy-remote.sh"
    [IO.File]::WriteAllText($localScript, ($remoteBody -replace "`r`n", "`n"), [Text.UTF8Encoding]::new($false))
    $deployEnvNames = @("SILICONFLOW_API_KEY", "SILICONFLOW_BASE_URL", "SILICONFLOW_MODEL", "SILICONFLOW_TIMEOUT_SECONDS", "SILICONFLOW_BOT_THINK_SECONDS")
    $deployEnvLines = foreach ($name in $deployEnvNames) {
        $match = Get-Content -LiteralPath $dotenvPath | Where-Object { $_ -match "^\s*$name\s*=" } | Select-Object -First 1
        if ($null -eq $match) { continue }
        $value = ($match -split "=", 2)[1].Trim().Trim('"').Trim("'")
        $shellQuoteEscape = ([string][char]39) + ([char]34) + ([char]39) + ([char]34) + ([char]39)
        $escaped = $value.Replace([string][char]39, $shellQuoteEscape)
        "$name='$escaped'"
    }
    if ($deployEnvLines.Count -gt 0) {
        $localEnv = Join-Path $tempRoot "deploy.env"
        [IO.File]::WriteAllText($localEnv, (($deployEnvLines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
        Run scp @($localEnv, "${RemoteHost}:$remoteEnv")
    }
    Run scp @($archive, "${RemoteHost}:$remoteArchive")
    Run scp @($localScript, "${RemoteHost}:$remoteScript")
    Run ssh @($RemoteHost, "bash $remoteScript; status=`$?; rm -f $remoteScript; exit `$status")
    Write-Host "部署完成。远端备份：${RemoteDirectory}-backup-$stamp"
}
finally {
    $resolved = [IO.Path]::GetFullPath($tempRoot)
    if ($resolved.StartsWith([IO.Path]::GetFullPath($tempBase), [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}