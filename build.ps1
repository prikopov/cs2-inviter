param()
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$metadata = Join-Path $env:WINDIR 'System32/WinMetadata'
$output = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $output | Out-Null
$references = foreach ($name in 'Windows.Foundation','Windows.Media','Windows.Graphics','Windows.Globalization','Windows.Storage') {
    "/reference:$metadata/$name.winmd"
}
$sources = (Get-ChildItem -LiteralPath "$PSScriptRoot/src" -Filter '*.cs').FullName
& "$framework/csc.exe" /nologo /platform:x64 /target:winexe "/out:$output/CS2Inviter.exe" "/reference:$framework/System.Runtime.WindowsRuntime.dll" "/reference:$framework/System.Runtime.dll" "/reference:$framework/System.Runtime.InteropServices.WindowsRuntime.dll" "/resource:$PSScriptRoot/assets/cover.png,cover.png" $references $sources
if ($LASTEXITCODE -ne 0) { throw "Ошибка компиляции ($LASTEXITCODE)." }
Write-Output "$output/CS2Inviter.exe"
