param([switch]$Deploy)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'MainProject/Infrastructure/Offline/Prepare-OfflineAssets.ps1')
$package = Join-Path $PSScriptRoot 'publish\ResilientTranslator'
$payload = Join-Path $package 'AppData'
$project = Join-Path $PSScriptRoot 'ResilientTranslator\ResilientTranslator.csproj'
$launcher = Join-Path $PSScriptRoot 'Launcher\Launcher.csproj'
dotnet publish $project -f net10.0-windows10.0.19041.0 -r win-x64 -c Release --self-contained false -p:PublishReadyToRun=false -p:RuntimeIdentifiers=win-x64 -p:NuGetAudit=false -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
dotnet publish $launcher -c Release -p:NuGetAudit=false -o $package
if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }
& 'C:\E\kp\aaBenimProgramlarim\Imza\Imzala.ps1' (Join-Path $package 'ResilientTranslator.exe') (Join-Path $payload 'ResilientTranslator.exe')
foreach ($file in @((Join-Path $package 'ResilientTranslator.exe'),(Join-Path $payload 'ResilientTranslator.exe'))) {
    if ((Get-AuthenticodeSignature -LiteralPath $file).Status -ne 'Valid') { throw "Invalid executable signature: $file" }
}
if ($Deploy) {
    $destination = 'C:\E\kp\aaBenimProgramlarim\ResilientTranslator'
    foreach ($running in @(Get-Process ResilientTranslator -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $destination 'AppData\ResilientTranslator.exe'))) {
        Stop-Process -Id $running.Id
        if (!$running.WaitForExit(5000)) { throw 'The installed application did not exit before deployment.' }
    }
    New-Item -ItemType Directory -Force $destination | Out-Null
    Copy-Item -Path (Join-Path $package '*') -Destination $destination -Recurse -Force
}
Write-Output $package
