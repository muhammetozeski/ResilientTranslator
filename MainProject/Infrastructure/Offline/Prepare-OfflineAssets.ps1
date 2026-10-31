param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$assetsRoot = Join-Path $projectRoot 'MainProject/wwwroot/offline'
$modelsRoot = [IO.Path]::GetFullPath((Join-Path $assetsRoot 'models')) + [IO.Path]::DirectorySeparatorChar
$manifest = Get-Content (Join-Path $assetsRoot 'models.json') -Raw | ConvertFrom-Json -AsHashtable
$files = @($manifest.Values | ForEach-Object { $_.Values })
$files | ForEach-Object -Parallel {
    $asset = $_
    $target = [IO.Path]::GetFullPath((Join-Path $using:assetsRoot $asset.path))
    if (!$target.StartsWith($using:modelsRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Model path escapes the model directory: $target" }
    if ((Test-Path -LiteralPath $target) -and (Get-Item -LiteralPath $target).Length -eq $asset.bytes -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $asset.sha256) { return }
    if ($using:ValidateOnly) { throw "Missing or invalid offline asset: $target" }
    New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
    $download = $target + '.partial'
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Invoke-WebRequest $asset.url -OutFile $download -TimeoutSec 180
            if ((Get-Item -LiteralPath $download).Length -ne $asset.bytes -or (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $asset.sha256) { throw "Downloaded model integrity mismatch: $($asset.path)" }
            Move-Item -LiteralPath $download -Destination $target -Force
            Write-Output "Verified offline model: $($asset.path)"
            break
        }
        catch {
            Write-Warning "Offline model attempt $attempt failed: $($asset.path); $_"
            if ($attempt -eq 3) { throw }
        }
    }
} -ThrottleLimit 4
Write-Output "Offline model inventory verified: $($files.Count) files"
