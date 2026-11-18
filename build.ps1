[CmdletBinding()]
param([string]$OutFile = (Join-Path $PSScriptRoot 'PeriLink.exe'))
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$metadata = Join-Path $env:WINDIR 'System32\WinMetadata'
$refs = @(
    (Join-Path $framework 'System.Runtime.WindowsRuntime.dll')
    (Join-Path $framework 'System.Runtime.dll')
    (Join-Path $framework 'System.Xaml.dll')
    (Join-Path $framework 'WPF\WindowsBase.dll')
    (Join-Path $framework 'WPF\PresentationCore.dll')
    (Join-Path $framework 'WPF\PresentationFramework.dll')
    (Join-Path $metadata 'Windows.Foundation.winmd')
    (Join-Path $metadata 'Windows.Devices.winmd')
    (Join-Path $metadata 'Windows.Storage.winmd')
)
$compilerFlags = @(
    '/nologo', '/utf8output', '/optimize+', '/target:winexe', '/platform:anycpu',
    "/win32manifest:$PSScriptRoot\LinkScope.manifest", "/out:$OutFile"
)
$icon=Join-Path $PSScriptRoot 'LinkScope.ico'
if(Test-Path -LiteralPath $icon) { $compilerFlags += "/win32icon:$icon" }
foreach($asset in @('LunaFury.png','BrandMarks.tsv','atk.png','vgn.png','mchose.jpg','gamesir.png')) {
    $compilerFlags += "/resource:$PSScriptRoot\$asset,LinkScope.$asset"
}
$references = $refs | ForEach-Object { "/r:$_" }
$files = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object { $_.FullName }
& (Join-Path $framework 'csc.exe') @compilerFlags @references @files
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
