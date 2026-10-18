param([switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$build = Join-Path $project '.build'
$portable = Join-Path $project 'dist\CodexUsageWidget-1.2.0-win-x64'
$distribution = Join-Path $project 'dist'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$native = Join-Path $build 'downloads\0.163.0-win32-x64\package\vendor\x86_64-pc-windows-msvc\bin\codex.exe'
if (-not (Test-Path -LiteralPath $native)) { throw 'Run packaging\download-runtime.ps1 first.' }
New-Item -ItemType Directory -Path $portable,(Join-Path $portable 'runtime'),(Join-Path $portable 'licenses'),$build -Force | Out-Null

# Build the small widget icon using Windows drawing components.
Add-Type -AssemblyName System.Drawing
$bitmap = [Drawing.Bitmap]::new(64,64)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$navy = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#14242E'))
$aqua = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#63E3CD'))
$pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#C3FFF5'),2)
$graphics.FillEllipse($navy,1,1,62,62)
$graphics.SetClip([Drawing.Rectangle]::new(9,32,46,25))
$graphics.FillEllipse($aqua,9,9,46,46)
$graphics.ResetClip()
$graphics.DrawArc($pen,9,9,46,46,195,150)
$graphics.DrawBezier($pen,10,34,23,24,39,44,54,31)
$icon = [Drawing.Icon]::FromHandle($bitmap.GetHicon())
$iconPath = Join-Path $build 'widget.ico'
$stream = [IO.File]::Create($iconPath)
try { $icon.Save($stream) } finally { $stream.Dispose(); $icon.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $navy.Dispose(); $aqua.Dispose(); $pen.Dispose() }

$shared = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output','/codepage:65001',('/win32icon:'+$iconPath),('/win32manifest:'+(Join-Path $project 'packaging\app.manifest')),('/reference:System.Windows.Forms.dll'),('/reference:System.Drawing.dll'))
& $compiler @shared ('/out:'+(Join-Path $portable 'CodexQuotaWidget.exe')) '/reference:System.Web.Extensions.dll' (Join-Path $project 'CodexAccountReader.cs') (Join-Path $project 'WidgetV2.cs') (Join-Path $project 'WidgetCompact.cs') (Join-Path $project 'packaging\AssemblyInfo.cs')
if ($LASTEXITCODE -ne 0) { throw 'Widget compilation failed.' }
& $compiler @shared ('/out:'+(Join-Path $portable 'Uninstall.exe')) (Join-Path $project 'packaging\InstallCommon.cs') (Join-Path $project 'packaging\Uninstall.cs') (Join-Path $project 'packaging\AssemblyInfo.cs')
if ($LASTEXITCODE -ne 0) { throw 'Uninstaller compilation failed.' }
Copy-Item -LiteralPath (Join-Path $project 'packaging\App.config') -Destination (Join-Path $portable 'CodexQuotaWidget.exe.config') -Force
Copy-Item -LiteralPath $native -Destination (Join-Path $portable 'runtime\codex.exe') -Force
Copy-Item -LiteralPath (Join-Path $project 'packaging\使用说明.txt') -Destination (Join-Path $portable '使用说明.txt') -Force
foreach ($name in @('CODEX-LICENSE.txt','CODEX-NOTICE.txt','THIRD-PARTY.txt')) {
    Copy-Item -LiteralPath (Join-Path $project "packaging\licenses\$name") -Destination (Join-Path $portable "licenses\$name") -Force
}
[IO.File]::WriteAllText((Join-Path $portable 'install.id'), 'CodexUsageWidget/1.0', [Text.Encoding]::ASCII)

# This explicit list excludes settings, cached values, accounts, credentials, previews and developer files.
$allowed = @('CodexQuotaWidget.exe','CodexQuotaWidget.exe.config','Uninstall.exe','使用说明.txt','install.id','runtime/codex.exe','licenses/CODEX-LICENSE.txt','licenses/CODEX-NOTICE.txt','licenses/THIRD-PARTY.txt')
$actual = @(Get-ChildItem -LiteralPath $portable -File -Recurse | ForEach-Object { $_.FullName.Substring($portable.Length+1).Replace('\','/') })
if (@(Compare-Object $allowed $actual).Count -ne 0) { throw 'Unexpected files in distribution directory. Refusing to package.' }
Write-Output "Native widget built; allowlisted payload files=$($actual.Count)"
if ($SkipInstaller) { exit 0 }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $build 'WidgetPayload.zip'
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
[IO.Compression.ZipFile]::CreateFromDirectory($portable,$archive,[IO.Compression.CompressionLevel]::Optimal,$false)
$setup = Join-Path $distribution 'CodexUsageWidget-Setup-1.2.0-win-x64.exe'
& $compiler @shared ('/out:'+$setup) '/reference:System.IO.Compression.dll' '/reference:System.IO.Compression.FileSystem.dll' ('/resource:'+$archive+',WidgetPayload.zip') (Join-Path $project 'packaging\InstallCommon.cs') (Join-Path $project 'packaging\Setup.cs') (Join-Path $project 'packaging\AssemblyInfo.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$zip = Join-Path $distribution 'CodexUsageWidget-Portable-1.2.0-win-x64.zip'
Copy-Item -LiteralPath $archive -Destination $zip -Force
$hashes = foreach ($file in @($setup,$zip)) { '{0}  {1}' -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(),[IO.Path]::GetFileName($file) }
[IO.File]::WriteAllLines((Join-Path $distribution 'SHA256SUMS.txt'),$hashes,[Text.Encoding]::ASCII)
Get-Item -LiteralPath $setup,$zip | Select-Object Name,Length
