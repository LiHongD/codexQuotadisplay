$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$project=Split-Path $PSScriptRoot -Parent
$cache=Join-Path $project '.build\downloads'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$version='0.163.0-win32-x64'
$metadata=Invoke-RestMethod -Uri "https://registry.npmjs.org/@openai/codex/$version" -TimeoutSec 30
if ($metadata.license -ne 'Apache-2.0' -or $metadata.version -ne $version) { throw 'Unexpected runtime package metadata.' }
$file=Join-Path $cache "codex-$version.tgz"
Invoke-WebRequest -UseBasicParsing -Uri $metadata.dist.tarball -OutFile $file -TimeoutSec 240
$sha=[Security.Cryptography.SHA512]::Create()
try { $integrity='sha512-'+[Convert]::ToBase64String($sha.ComputeHash([IO.File]::ReadAllBytes($file))) } finally { $sha.Dispose() }
if ($integrity -ne $metadata.dist.integrity) { throw 'Runtime package integrity check failed.' }
$destination=Join-Path $cache $version
New-Item -ItemType Directory -Path $destination -Force | Out-Null
& tar -xzf $file -C $destination
if ($LASTEXITCODE -ne 0) { throw 'Extraction failed.' }
Write-Output "Verified official Codex runtime $version"
