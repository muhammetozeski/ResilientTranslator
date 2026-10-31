$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$values=@{}
foreach($file in Get-ChildItem (Join-Path $PSScriptRoot 'translations') -Filter '*.json'){
    $part=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json -AsHashtable
    foreach($key in $part.Keys){if($values.ContainsKey($key)){throw "Duplicate feature translation key: $key"};$values[$key]=$part[$key]}
}
$cultures=@('tr','en','de','fr','es','it','ru')
$fields=[Collections.Generic.List[string]]::new()
$fields.Add('namespace ResilientTranslator.MainProject.Infrastructure.Localization;')
$fields.Add('/// <summary>Feature resources loaded through the existing XML localization manager.</summary>')
$fields.Add('public static partial class Strings')
$fields.Add('{')
foreach($key in ($values.Keys|Sort-Object)){
    if($key -notmatch '^[A-Za-z][A-Za-z0-9]*$'){throw "Invalid resource field: $key"}
    foreach($culture in $cultures){if([string]::IsNullOrWhiteSpace($values[$key][$culture])){throw "Missing translation: $key/$culture"}}
    $literal=ConvertTo-Json -InputObject ([string]$values[$key].tr) -Compress
    $fields.Add("    public static string $key = $literal;")
}
$fields.Add('}')
[IO.File]::WriteAllLines((Join-Path $root 'MainProject/Infrastructure/Localization/Strings.Features.cs'),$fields)
foreach($culture in $cultures){
    $file=Join-Path $root "languages/lang.$culture.xml"
    $xml=[Xml.Linq.XDocument]::Load($file)
    foreach($key in ($values.Keys|Sort-Object)){
        $element=$xml.Root.Elements('s')|Where-Object {$_.Attribute('name').Value -eq $key}|Select-Object -First 1
        if($null -eq $element){$element=[Xml.Linq.XElement]::new([Xml.Linq.XName]::Get('s'));$element.SetAttributeValue('name',$key);$xml.Root.Add($element)}
        $element.Value=[string]$values[$key][$culture]
    }
    $xml.Save($file)
}
Write-Output "Merged $($values.Count) feature keys into $($cultures.Count) language files."
