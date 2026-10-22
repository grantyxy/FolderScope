$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkPath = Split-Path -Parent $compilerPath
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Xml.dll', 'System.Security.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'Microsoft.CSharp.dll')
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/unsafe', '/codepage:65001', ('/out:' + (Join-Path $projectDir 'FolderScope.exe')), ('/win32manifest:' + (Join-Path $projectDir 'app.manifest')))
foreach ($assembly in $references) { $arguments += '/reference:' + (Join-Path $frameworkPath $assembly) }
$vbPath = Join-Path $frameworkPath 'Microsoft.VisualBasic.dll'
if (-not (Test-Path -LiteralPath $vbPath)) {
    $vbPath = Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\Microsoft.VisualBasic') -Filter Microsoft.VisualBasic.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
}
$arguments += '/reference:' + $vbPath
$arguments += Get-ChildItem -LiteralPath (Join-Path $projectDir 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compilerPath @arguments
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
Get-Item -LiteralPath (Join-Path $projectDir 'FolderScope.exe') | Select-Object FullName, Length, LastWriteTime
