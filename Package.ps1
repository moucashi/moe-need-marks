[CmdletBinding()]
param(
    [string]$SPTPath = (Join-Path $PSScriptRoot '../../客户端'),
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
$spt = (Resolve-Path -LiteralPath $SPTPath).Path
$repo = $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
$dist = Join-Path $repo 'dist'
$staging = Join-Path $dist ('staging/' + [guid]::NewGuid().ToString('N'))
$clientRelative = 'BepInEx/plugins/Moe-NeedMarks'
$serverRelative = 'SPT_Runtime/user/mods/Moe-NeedMarks'
New-Item -ItemType Directory -Path (Join-Path $staging $clientRelative), (Join-Path $staging $serverRelative) -Force | Out-Null
foreach ($project in @('Client/MoeNeedMarks.Client.csproj', 'Server/MoeNeedMarks.Server.csproj')) {
    & dotnet build (Join-Path $repo $project) -c Release --nologo "-p:SPTPath=$spt"
    if ($LASTEXITCODE -ne 0) { throw "构建失败：$project" }
}
$previousTestPath = $env:NEEDMARKS_SPT_PATH
try {
    $env:NEEDMARKS_SPT_PATH = $spt
    & dotnet test (Join-Path $repo 'Tests/MoeNeedMarks.Tests.csproj') -c Release --nologo --logger 'trx;LogFileName=tests.trx' --results-directory (Join-Path $repo 'verification/runtime/test-results')
    if ($LASTEXITCODE -ne 0) { throw '测试失败，未生成安装包。' }
} finally { $env:NEEDMARKS_SPT_PATH = $previousTestPath }
$sourceFiles = [ordered]@{
    "$clientRelative/MoeNeedMarks.Client.dll" = 'Client/bin/Release/net472/MoeNeedMarks.Client.dll'
    "$serverRelative/MoeNeedMarks.Server.dll" = 'Server/bin/Release/net10.0/MoeNeedMarks.Server.dll'
    "$serverRelative/MoeNeedMarks.Shared.dll" = 'Server/bin/Release/net10.0/MoeNeedMarks.Shared.dll'
    "$serverRelative/MoeNeedMarks.Server.deps.json" = 'Server/bin/Release/net10.0/MoeNeedMarks.Server.deps.json'
    "$serverRelative/README.md" = 'README.md'
    "$serverRelative/verification/验收记录.md" = 'verification/验收记录.md'
    "$serverRelative/LICENSE" = 'LICENSE'
}
foreach ($entry in $sourceFiles.GetEnumerator()) {
    $source = Join-Path $repo $entry.Value
    if ($entry.Key.EndsWith('.dll') -and [Reflection.AssemblyName]::GetAssemblyName($source).Version.ToString() -ne "$version.0") { throw "程序集版本不一致：$source" }
    New-Item -ItemType Directory -Path (Split-Path (Join-Path $staging $entry.Key)) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination (Join-Path $staging $entry.Key)
}
$hashes = foreach ($relative in $sourceFiles.Keys) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $staging $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
}
$hashes | Set-Content -LiteralPath (Join-Path $staging 'Moe-NeedMarks-SHA256.txt') -Encoding utf8NoBOM
$archive = Join-Path $dist "Moe-NeedMarks-$version-SPT4.1.5.zip"
$tempArchive = Join-Path $dist ([guid]::NewGuid().ToString('N') + '.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($staging, $tempArchive)
[IO.File]::Move($tempArchive, $archive, $true)
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath ($archive + '.sha256') -Encoding utf8NoBOM
if ($Install) {
    foreach ($relative in $sourceFiles.Keys) {
        $target = Join-Path $spt $relative
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $staging $relative) -Destination $target
        if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath (Join-Path $staging $relative)).Hash) { throw "安装校验失败：$relative" }
    }
    Copy-Item -LiteralPath (Join-Path $staging 'Moe-NeedMarks-SHA256.txt') -Destination $spt
    Write-Output "已安装并校验：$spt"
}
Write-Output "安装包：$archive"
Write-Output "SHA256：$archiveHash"
