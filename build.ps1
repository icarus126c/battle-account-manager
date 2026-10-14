param([ValidateSet('personal','friends','sponsored')][string]$Edition='personal', [string]$OutputDirectory = (Join-Path $PSScriptRoot 'release'), [string]$DependencyDirectory = (Join-Path $PSScriptRoot 'deps'))
$ErrorActionPreference = 'Stop'
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$targetDir = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
$references = @('System.dll','System.Management.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Core.dll','System.Xaml.dll','System.Security.dll','System.Runtime.Serialization.dll','System.Web.Extensions.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkDir $_) }
$references += @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path (Join-Path $frameworkDir 'WPF') $_) }
$sourceDir = Join-Path $PSScriptRoot 'src'
$managedSdk = [IO.Path]::GetFullPath($DependencyDirectory)
$references += @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll') | ForEach-Object { '/reference:' + (Join-Path $managedSdk $_) }
$sources = Get-ChildItem -LiteralPath $sourceDir -Filter '*.cs' | ForEach-Object FullName
& (Join-Path $frameworkDir 'csc.exe') /nologo /target:winexe /platform:x86 /optimize+ /utf8output ('/define:EDITION_'+$Edition.ToUpperInvariant()) ('/resource:'+(Join-Path $sourceDir 'Assets\alipay.png')+',BattleAccountManager.Support.Alipay.png') ('/out:' + (Join-Path $targetDir 'BattleAccountManager.exe')) ('/win32manifest:' + (Join-Path $sourceDir 'app.manifest')) ('/win32icon:' + (Join-Path $sourceDir 'app.ico')) @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath (Join-Path $sourceDir 'app.config') -Destination (Join-Path $targetDir 'BattleAccountManager.exe.config')
@('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll','WebView2Loader.dll','WebView2-LICENSE.txt','WebView2-NOTICE.txt') | ForEach-Object { Copy-Item -LiteralPath (Join-Path $managedSdk $_) -Destination $targetDir }
$engine=Join-Path $targetDir 'Engine'
New-Item -ItemType Directory -Force -Path $engine | Out-Null
$tcnoAssets=Join-Path $managedSdk 'Engine'
Copy-Item -LiteralPath (Join-Path $tcnoAssets 'TcNo-Acc-Switcher.exe'),(Join-Path $tcnoAssets 'Platforms-v4.0.8.json'),(Join-Path $tcnoAssets 'LICENSE') -Destination $engine
& (Join-Path $frameworkDir 'csc.exe') /nologo /target:winexe /platform:x86 /optimize+ /utf8output ('/define:EDITION_'+$Edition.ToUpperInvariant()) ('/out:' + (Join-Path $targetDir '启动账号管家.exe')) ('/win32manifest:' + (Join-Path $sourceDir 'app.manifest')) ('/win32icon:' + (Join-Path $sourceDir 'app.ico')) /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'startup\Launcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed' }
Write-Output (Join-Path $targetDir 'BattleAccountManager.exe')
