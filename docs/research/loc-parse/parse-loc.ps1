param(
  [Parameter(Mandatory = $true)][string]$Path,
  [string]$OutJson
)

# Mirrors LocaleAsset.Load (src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/LocaleAsset.cs:109-134):
# ushort formatVersion, string systemLanguage, string localeId, string localizedName,
# int entryCount, entryCount x (string key, string value),
# int indexCountCount, that many x (string key, int count).
# BinaryWriter.Write(string) is a 7-bit-encoded byte length followed by UTF-8 bytes,
# which is exactly what BinaryReader.ReadString() undoes, so no hand-rolled string reader is needed.

$fs = [System.IO.File]::OpenRead($Path)
$br = New-Object System.IO.BinaryReader($fs, [System.Text.Encoding]::UTF8)

$formatVersion = $br.ReadUInt16()
$systemLanguage = $br.ReadString()
$localeId = $br.ReadString()
$localizedName = $br.ReadString()

$entryCount = $br.ReadInt32()
$entries = New-Object 'System.Collections.Generic.Dictionary[string,string]'
for ($i = 0; $i -lt $entryCount; $i++) {
  $k = $br.ReadString()
  $v = $br.ReadString()
  $entries[$k] = $v
}

$indexCount = $br.ReadInt32()
$indexCounts = New-Object 'System.Collections.Generic.Dictionary[string,int]'
for ($i = 0; $i -lt $indexCount; $i++) {
  $k = $br.ReadString()
  $indexCounts[$k] = $br.ReadInt32()
}

$trailing = $fs.Length - $fs.Position
$br.Dispose()
$fs.Dispose()

Write-Output "file            : $Path"
Write-Output "formatVersion   : $formatVersion"
Write-Output "systemLanguage  : $systemLanguage"
Write-Output "localeId        : $localeId"
Write-Output "localizedName   : $localizedName"
Write-Output "entries         : $entryCount (distinct keys: $($entries.Count))"
Write-Output "indexCounts     : $indexCount (distinct keys: $($indexCounts.Count))"
Write-Output "trailing bytes  : $trailing"

if ($OutJson) {
  $payload = [ordered]@{
    formatVersion  = $formatVersion
    systemLanguage = $systemLanguage
    localeId       = $localeId
    localizedName  = $localizedName
    entries        = $entries
    indexCounts    = $indexCounts
  }
  $json = $payload | ConvertTo-Json -Depth 5 -Compress
  [System.IO.File]::WriteAllText($OutJson, $json, (New-Object System.Text.UTF8Encoding($false)))
  Write-Output "wrote           : $OutJson"
}
