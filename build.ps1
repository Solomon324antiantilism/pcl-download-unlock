param([string]$OutputPath = (Join-Path $PSScriptRoot 'dist\PCL下载恢复工具.exe'))
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'The .NET Framework C# compiler was not found.' }
$dependencyPath = Join-Path $PSScriptRoot 'lib\dnlib.dll'
$expectedHash = '97EF5328D1D1703924C48D9CD1D45B7227FE3836E116DCDED623B6A0DBD52C23'
if ((Get-FileHash -LiteralPath $dependencyPath -Algorithm SHA256).Hash -ne $expectedHash) { throw 'The pinned dnlib.dll SHA256 does not match.' }
$absoluteOutput = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $absoluteOutput) -Force | Out-Null
$compilerArgs = @('/nologo', '/target:winexe', '/platform:x86', '/optimize+', '/codepage:65001', "/out:$absoluteOutput", "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')", "/r:$dependencyPath", '/r:System.Core.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll', "/resource:$dependencyPath,PclDownloadUnlock.dnlib.dll", "/resource:$(Join-Path $PSScriptRoot 'lib\LICENSE-dnlib.txt'),PclDownloadUnlock.LICENSE-dnlib.txt")
$compilerArgs += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compilerPath @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed: $LASTEXITCODE" }
Get-FileHash -LiteralPath $absoluteOutput -Algorithm SHA256
