param([Parameter(Mandatory=$true)][string]$OriginalPcl, [string]$WorkDirectory = (Join-Path $env:TEMP ('PclUnlockTests-' + [Guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Path $WorkDirectory -Force | Out-Null
$testExe = Join-Path $WorkDirectory 'CoreTests.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$testArgs = @('/nologo', '/target:exe', '/platform:x86', '/codepage:65001', '/main:CoreTests', "/out:$testExe", "/r:$(Join-Path $sourceRoot 'lib\dnlib.dll')", '/r:System.Core.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll', "/resource:$(Join-Path $sourceRoot 'lib\dnlib.dll'),PclDownloadUnlock.dnlib.dll", "/resource:$(Join-Path $sourceRoot 'lib\LICENSE-dnlib.txt'),PclDownloadUnlock.LICENSE-dnlib.txt")
$testArgs += Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$testArgs += Join-Path $PSScriptRoot 'CoreTests.cs'
& $compiler @testArgs
if ($LASTEXITCODE -ne 0) { throw 'Test runner compilation failed.' }
$processFixture = Join-Path $WorkDirectory 'PCL Process Test.exe'
$processPlatform = if ([Environment]::Is64BitOperatingSystem) { '/platform:x64' } else { '/platform:x86' }
& $compiler /nologo /target:exe $processPlatform "/out:$processFixture" (Join-Path $PSScriptRoot 'ProcessFixture.cs')
if ($LASTEXITCODE -ne 0) { throw 'Process fixture compilation failed.' }
& $testExe $OriginalPcl (Join-Path $WorkDirectory 'fixtures') $processFixture
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
