$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$issues = [Collections.Generic.List[string]]::new()
$layout = Get-Content -LiteralPath (Join-Path $root 'MainProject\UI\Layout\MainLayout.razor') -Raw
$layout = [regex]::Replace($layout, '(?s)@\*.*?\*@|<!--.*?-->', '')
if ($layout -notmatch '<MainLayoutCss(?:\s|/)') { $issues.Add('MainLayout does not render MainLayoutCss.') }
if ($layout -notmatch '<CascadingValue\s+Value="ThemeManager.Current"') { $issues.Add('MainLayout does not cascade its active theme.') }
$imports = Get-Content -LiteralPath (Join-Path $root 'MainProject\UI\_Imports.razor') -Raw
if ($imports -notmatch '@inherits\s+[^\r\n]*ThemeComponent') { $issues.Add('The shared component imports do not retain the theme-aware base.') }
$ui = Join-Path $root 'MainProject\UI'
foreach ($file in Get-ChildItem -LiteralPath $ui -Filter '*.razor.css' -Recurse -File) {
    $issues.Add("Scoped stylesheet bypasses the Css.razor convention: $($file.FullName)")
}
foreach ($page in Get-ChildItem -LiteralPath (Join-Path $ui 'Pages') -Filter '*.razor' -File) {
    $text = [IO.File]::ReadAllText($page.FullName)
    if ($text -notmatch '@(?:page\b|attribute\s*\[\s*Route)') { continue }
    $companion = $page.BaseName + 'Css'
    if ((Test-Path -LiteralPath (Join-Path $page.DirectoryName ($companion + '.razor'))) -and $text -notmatch ('<' + [regex]::Escape($companion) + '(?:\s|/)')) {
        $issues.Add("Page does not render its style companion: $($page.Name)")
    }
}
$shared = Join-Path $root 'MainProject'
$sourceFiles = @(Get-ChildItem -LiteralPath $shared -Filter '*.cs' -File)
foreach ($directory in Get-ChildItem -LiteralPath $shared -Directory | Where-Object Name -notin 'bin','obj','packages') {
    $sourceFiles += Get-ChildItem -LiteralPath $directory.FullName -Filter '*.cs' -Recurse -File
}
$identities = @($sourceFiles | Where-Object { [IO.File]::ReadAllText($_.FullName) -match '\bclass\s+AppConstants\b' })
if ($identities.Count -gt 1) { $issues.Add('More than one AppConstants class exists in MainProject; inspect duplicate identity centers.') }
if ($issues.Count) { $issues | ForEach-Object { Write-Output "FAIL: $_" }; exit 1 }
Write-Output 'PASS: Root CSS, theme propagation, page style companions and application identity source connections.'
Write-Output 'This source check does not prove runtime behavior or complete component reuse.'
