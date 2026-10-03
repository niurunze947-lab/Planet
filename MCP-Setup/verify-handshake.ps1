# verify-handshake.ps1
# Performs a REAL MCP handshake (initialize -> notifications/initialized -> tools/list)
# against the MCP for Unity stdio server, exactly as a client (DSH/codex/opencode/MiMo) would.
# This proves server startup + protocol negotiation + tool registration without driving Unity.

param(
    [string]$UvxPath = "C:\Users\nrz\AppData\Local\Microsoft\WinGet\Links\uvx.exe",
    [string]$Package  = "mcpforunityserver==10.0.0",
    [int]$TimeoutSec  = 90
)

$ErrorActionPreference = 'Stop'

function Send-JsonRpc {
    param($Writer, $Obj)
    $json = $Obj | ConvertTo-Json -Depth 10 -Compress
    $Writer.WriteLine($json)
    $Writer.Flush()
}

function Read-UntilId {
    param($Reader, $Id, $TimeoutSec)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $line = $Reader.ReadLine()
        if ($null -eq $line) { break }
        $line = $line.Trim()
        if ($line -eq '') { continue }
        if (-not $line.StartsWith('{')) { continue }   # skip any stray non-JSON output
        try { $o = $line | ConvertFrom-Json } catch { continue }
        if ($o.id -eq $Id) { return $o }
    }
    return $null
}

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName               = $UvxPath
$psi.Arguments              = "--from `"$Package`" mcp-for-unity --transport stdio"
$psi.RedirectStandardInput  = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError  = $true
$psi.UseShellExecute        = $false
$psi.CreateNoWindow         = $true

$proc = [System.Diagnostics.Process]::Start($psi)
$errTask = $proc.StandardError.ReadToEndAsync()

try {
    $stdin  = $proc.StandardInput
    $stdout = $proc.StandardOutput

    # 1. initialize
    Send-JsonRpc $stdin @{
        jsonrpc = "2.0"; id = 1; method = "initialize"
        params  = @{
            protocolVersion = "2024-11-05"
            capabilities    = @{}
            clientInfo      = @{ name = "mcp-handshake-verifier"; version = "1.0" }
        }
    }
    $init = Read-UntilId $stdout 1 $TimeoutSec
    if ($null -eq $init) { throw "No initialize response within ${TimeoutSec}s" }

    $serverInfo = $init.result.serverInfo
    Write-Output "HANDSHAKE: OK"
    Write-Output "  protocolVersion : $($init.result.protocolVersion)"
    Write-Output "  serverName      : $($serverInfo.name)"
    Write-Output "  serverVersion   : $($serverInfo.version)"

    # 2. initialized notification
    Send-JsonRpc $stdin @{ jsonrpc = "2.0"; method = "notifications/initialized" }

    # 3. tools/list
    Send-JsonRpc $stdin @{ jsonrpc = "2.0"; id = 2; method = "tools/list"; params = @{} }
    $tools = Read-UntilId $stdout 2 $TimeoutSec
    if ($null -eq $tools) { throw "No tools/list response within ${TimeoutSec}s" }

    $names = @($tools.result.tools | ForEach-Object { $_.name } | Sort-Object)
    Write-Output "  toolCount       : $($names.Count)"
    Write-Output ""
    Write-Output "TOOLS:"
    $names | ForEach-Object { Write-Output "  $_" }

    # 4. resources/list (best effort)
    Send-JsonRpc $stdin @{ jsonrpc = "2.0"; id = 3; method = "resources/list"; params = @{} }
    $res = Read-UntilId $stdout 3 20
    if ($res -and $res.result.resources) {
        Write-Output ""
        Write-Output "RESOURCES:"
        $res.result.resources | ForEach-Object { Write-Output "  $($_.uri)" }
    }
}
finally {
    try { if (-not $proc.HasExited) { $proc.Kill() } } catch {}
    Start-Sleep -Milliseconds 300
    $errText = $null
    try { $errText = $errTask.Result } catch {}
    $errLines = @($errText -split "`r?`n" | Where-Object { $_ -match 'error|Error|Traceback|Exception' } | Select-Object -First 5)
    if ($errLines.Count -gt 0) {
        Write-Output ""
        Write-Output "SERVER STDERR (errors only):"
        $errLines | ForEach-Object { Write-Output "  $_" }
    }
}
