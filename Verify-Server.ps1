[CmdletBinding()]
param(
    [string]$SPTPath = (Join-Path $PSScriptRoot '../../客户端'),
    [int]$Port = 26969
)
$ErrorActionPreference = 'Stop'
$spt = (Resolve-Path -LiteralPath $SPTPath).Path
$runtime = Join-Path $PSScriptRoot ('verification/runtime/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $spt 'SPT_Runtime') -File | Copy-Item -Destination $runtime
$data = Join-Path $runtime 'SPT_Data'
New-Item -ItemType Directory -Path $data | Out-Null
Copy-Item -LiteralPath (Join-Path $spt 'SPT_Runtime/SPT_Data/configs') -Destination $data -Recurse
foreach ($name in @('database', 'images', 'wwwroot')) {
    New-Item -ItemType Junction -Path (Join-Path $data $name) -Target (Join-Path $spt "SPT_Runtime/SPT_Data/$name") | Out-Null
}
Copy-Item -LiteralPath (Join-Path $spt 'SPT_Runtime/SPT_Data/checks.dat') -Destination $data
$httpPath = Join-Path $data 'configs/http.json'
$http = Get-Content -LiteralPath $httpPath -Raw | ConvertFrom-Json
$http.ip = '127.0.0.1'; $http.backendIp = '127.0.0.1'; $http.port = $Port; $http.backendPort = $Port
$http | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $httpPath -Encoding utf8NoBOM
$profilesDir = Join-Path $runtime 'user/profiles'
$modDir = Join-Path $runtime 'user/mods/Moe-NeedMarks'
New-Item -ItemType Directory -Path $profilesDir, $modDir -Force | Out-Null
$sourceProfile = Get-ChildItem -LiteralPath (Join-Path $spt 'SPT_Runtime/user/profiles') -Filter '*.json' | Select-Object -First 1
if (!$sourceProfile) { throw '没有可用于隔离验证的存档。' }
$sourceHash = (Get-FileHash -LiteralPath $sourceProfile.FullName).Hash
Copy-Item -LiteralPath $sourceProfile.FullName -Destination $profilesDir
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Server/bin/Release/net10.0') -File |
    Where-Object Name -Like 'MoeNeedMarks.*' | Copy-Item -Destination $modDir
# Copy mod code/config/databases for compatibility testing; large immutable Unity
# bundles can be shared, while every writable mod configuration stays isolated.
foreach ($mod in Get-ChildItem -LiteralPath (Join-Path $spt 'SPT_Runtime/user/mods') -Directory) {
    if ($mod.Name -eq 'Moe-NeedMarks') { continue }
    $destination = Join-Path $runtime ('user/mods/' + $mod.Name)
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $mod.FullName) {
        if ($entry.PSIsContainer -and $entry.Name -eq 'bundles') {
            New-Item -ItemType Junction -Path (Join-Path $destination $entry.Name) -Target $entry.FullName | Out-Null
        } else { Copy-Item -LiteralPath $entry.FullName -Destination $destination -Recurse }
    }
}
$fikaPath = Join-Path $runtime 'user/mods/fika-server/assets/configs/fika.jsonc'
if (Test-Path -LiteralPath $fikaPath) {
    $fika = Get-Content -LiteralPath $fikaPath -Raw | ConvertFrom-Json
    $fika.server.SPT.http.ip = '127.0.0.1'; $fika.server.SPT.http.backendIp = '127.0.0.1'
    $fika.server.SPT.http.port = $Port; $fika.server.SPT.http.backendPort = $Port
    $fika.server.webhook.enabled = $false
    $fika.headless.scripts.generate = $false
    $fika | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $fikaPath -Encoding utf8NoBOM
}
function Get-NeedSnapshot {
    $reply = Invoke-WebRequest -Uri "https://127.0.0.1:$Port/moe/needmarks/snapshot" -Headers @{ Cookie = "PHPSESSID=$($sourceProfile.BaseName)" } -SkipCertificateCheck -TimeoutSec 10
    $bytes = $reply.RawContentStream.ToArray()
    if ($bytes.Length -gt 2 -and $bytes[0] -eq 0x78) {
        $memory = [IO.MemoryStream]::new($bytes)
        $zlib = [IO.Compression.ZLibStream]::new($memory, [IO.Compression.CompressionMode]::Decompress)
        $reader = [IO.StreamReader]::new($zlib, [Text.Encoding]::UTF8)
        try { $body = $reader.ReadToEnd() } finally { $reader.Dispose(); $zlib.Dispose(); $memory.Dispose() }
    } else { $body = [Text.Encoding]::UTF8.GetString($bytes) }
    return $body | ConvertFrom-Json
}
$stdout = Join-Path $runtime 'stdout.log'; $stderr = Join-Path $runtime 'stderr.log'
$server = $null
try {
    $server = Start-Process -FilePath (Join-Path $runtime 'SPT.Server.exe') -WorkingDirectory $runtime -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    Write-Output "隔离服务端 PID=$($server.Id)，日志目录=$runtime"
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    $response = $null; $lastFailure = ''
    while ([DateTime]::UtcNow -lt $deadline) {
        $server.Refresh()
        if ($server.HasExited) { throw "服务端提前退出：$(Get-Content -LiteralPath $stdout -Tail 30 | Out-String) $(Get-Content -LiteralPath $stderr -Tail 10 | Out-String)" }
        try {
            $response = Get-NeedSnapshot
            break
        } catch { $lastFailure = $_.Exception.Message; Start-Sleep -Milliseconds 500 }
    }
    if (!$response) { throw "接口未就绪：$lastFailure" }
    if ($response.Schema -ne 1 -or !$response.ProfileId -or $response.Quests.Count -lt 100 -or $response.Areas.Count -lt 10) {
        throw "快照格式异常，Schema=$($response.Schema)，任务数=$($response.Quests.Count)"
    }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $second = Get-NeedSnapshot
    $watch.Stop()
    if ($second.ProfileId -ne $response.ProfileId) { throw '重复请求串号。' }
    $report = [ordered]@{ Version = '1.0.0'; SPT = '4.1.5'; Schema = $response.Schema; Quests = $response.Quests.Count; AreaStages = $response.Areas.Count; StashObjects = $response.Stash.Count; WarmRequestMilliseconds = $watch.ElapsedMilliseconds; SourceProfileUnchanged = ((Get-FileHash -LiteralPath $sourceProfile.FullName).Hash -eq $sourceHash); Runtime = $runtime }
    if (!$report.SourceProfileUnchanged) { throw '源存档发生变化。' }
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'verification/server-smoke.json') -Encoding utf8NoBOM
    $report | ConvertTo-Json
} finally {
    if ($server) {
        $server.Refresh()
        if (!$server.HasExited) { Stop-Process -Id $server.Id; $server.WaitForExit(10000) | Out-Null }
        Write-Output "隔离服务端 PID=$($server.Id) 已停止。"
    }
}
