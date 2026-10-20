param([string]$WorkDirectory = (Join-Path $env:TEMP ('PclUnlockSmoke-' + [Guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $sourceRoot 'build.ps1')
New-Item -ItemType Directory -Path $WorkDirectory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$testExe = Join-Path $WorkDirectory 'SmokeTests.exe'
$compile = @('/nologo', '/target:exe', '/platform:x86', '/codepage:65001', '/main:SmokeTests', "/out:$testExe", "/r:$(Join-Path $sourceRoot 'lib\dnlib.dll')", '/r:System.Core.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll', "/resource:$(Join-Path $sourceRoot 'lib\dnlib.dll'),PclDownloadUnlock.dnlib.dll", "/resource:$(Join-Path $sourceRoot 'lib\LICENSE-dnlib.txt'),PclDownloadUnlock.LICENSE-dnlib.txt")
$compile += Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$compile += Join-Path $PSScriptRoot 'SmokeTests.cs'
& $compiler @compile
if ($LASTEXITCODE -ne 0) { throw 'Smoke test compilation failed.' }
$dummy = Join-Path $WorkDirectory 'RenamedLauncher.exe'
& $compiler /nologo /target:exe /platform:x86 "/out:$dummy" (Join-Path $PSScriptRoot 'ProcessFixture.cs')
if ($LASTEXITCODE -ne 0) { throw 'Process fixture compilation failed.' }
& $testExe (Join-Path $WorkDirectory 'fixtures') $dummy
if ($LASTEXITCODE -ne 0) { throw 'Smoke tests failed.' }
