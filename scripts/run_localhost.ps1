[CmdletBinding()]
param(
    [ValidateRange(1, 65535)][int]$Port = 5000,
    [ValidateRange(1, 120)][int]$TimeoutSeconds = 30,
    [switch]$Rebuild,
    [switch]$SkipBuild,
    [switch]$EnableLlmBots,
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$serverProject = Join-Path $repoRoot "WereMFServer\WereMFServer.csproj"
$gameProject = Join-Path $repoRoot "WereMF\WereMF.fsproj"
$serverExecutable = Join-Path $repoRoot "WereMFServer\bin\Release\net8.0\WereMFServer.exe"
$gameExecutable = Join-Path $repoRoot "WereMF\bin\Release\net8.0\WereMF.exe"
$configPath = Join-Path $repoRoot "WereMF\config.json"
$dotenvPath = Join-Path $repoRoot ".env"
$stdoutPath = Join-Path ([IO.Path]::GetTempPath()) "weremf-localhost-run.stdout.log"
$stderrPath = Join-Path ([IO.Path]::GetTempPath()) "weremf-localhost-run.stderr.log"
$serverProcess = $null

function Invoke-Checked([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File 退出码 $LASTEXITCODE" }
}

function Get-Health {
    try { Invoke-RestMethod -Uri "http://127.0.0.1:$Port/api/health" -TimeoutSec 2 } catch { $null }
}

function Wait-ForHealth([int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        $health = Get-Health
        if ($null -ne $health -and $health.status -eq "ok") { return $health }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Import-DotEnv {
    if (-not (Test-Path -LiteralPath $dotenvPath -PathType Leaf)) { return }
    foreach ($line in Get-Content -LiteralPath $dotenvPath) {
        if ($line -match '^\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$') {
            $name = $Matches[1]
            $value = $Matches[2]
            if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) {
                $value = $value.Substring(1, $value.Length - 2)
            }
            if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name, "Process"))) {
                [Environment]::SetEnvironmentVariable($name, $value, "Process")
            }
        }
    }
    Write-Host "已加载 .env 环境变量（值不会输出）。"
}

try {
    Import-DotEnv
    if ($Rebuild -and $SkipBuild) { throw "-Rebuild 与 -SkipBuild 不能同时使用。" }
    $health = Wait-ForHealth 2
    if ($null -ne $health) {
        if ($Rebuild) { throw "端口 $Port 已有服务，无法执行 -Rebuild；请先关闭旧服务。" }
        if ($EnableLlmBots -and -not [bool]$health.llmBots) {
            throw "端口 $Port 上的现有服务禁用了 LLM Bot；请先关闭旧服务，再使用 -EnableLlmBots 启动。"
        }
        Write-Host "检测到已有本地服务，直接复用 http://127.0.0.1:$Port/（LLM Bot: $([bool]$health.llmBots)）"
    }
    else {
        if (-not $SkipBuild) {
            Write-Host "正在构建 WereMF 和 WereMFServer (Release)..."
            $buildArguments = @("build", $gameProject, "-c", "Release")
            if ($Rebuild) { $buildArguments += "--no-incremental" }
            Invoke-Checked -File "dotnet" -Arguments $buildArguments
            $buildArguments = @("build", $serverProject, "-c", "Release")
            if ($Rebuild) { $buildArguments += "--no-incremental" }
            Invoke-Checked -File "dotnet" -Arguments $buildArguments
        }
        if (-not (Test-Path -LiteralPath $serverExecutable)) { throw "找不到服务端可执行文件：$serverExecutable" }
        if (-not (Test-Path -LiteralPath $gameExecutable)) { throw "找不到游戏可执行文件：$gameExecutable" }
        if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) { throw "找不到配置文件：$configPath" }

        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
        $arguments = @("--path", $gameExecutable, "--config", $configPath, "--host", "127.0.0.1", "--port", $Port)
        if (-not $EnableLlmBots) { $arguments += "--disable-llm-bots" }
        $serverProcess = Start-Process -FilePath $serverExecutable `
            -ArgumentList $arguments -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

        $health = Wait-ForHealth $TimeoutSeconds
        if ($null -eq $health) {
            throw "本地服务启动失败；stdout=$stdoutPath；stderr=$stderrPath"
        }
        Write-Host "本地服务已启动 (PID $($serverProcess.Id)，LLM Bot: $([bool]$health.llmBots))"
    }

    $url = "http://127.0.0.1:$Port/"
    if ($NoBrowser) {
        Write-Host "浏览器启动已跳过：$url"
    }
    else {
        Start-Process $url
        Write-Host "浏览器已打开：$url"
    }
    Write-Host "现在可以直接游玩。关闭服务请回到此窗口按 Enter。"
    [void](Read-Host)
}
catch {
    Write-Error "localhost 启动失败：$($_.Exception.Message)"
    exit 1
}
finally {
    if ($null -ne $serverProcess -and -not $serverProcess.HasExited) {
        Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
        $serverProcess.WaitForExit()
        Write-Host "本次启动的本地服务已停止。"
    }
}
