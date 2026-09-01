param(
  [Parameter(Mandatory = $true)][string]$Json,
  [string]$OutCsv
)

# Splits every key in a decoded .loc into group / id / identifier type, using the four
# identifier regexes at src/Colossal.Localization/Colossal.Localization/LocalizationValidation.cs:22-25.

$doc = [System.IO.File]::ReadAllText($Json) | ConvertFrom-Json
$entries = $doc.entries

$rxSingle = [regex]'^(?!\d)([\w$]+)\.(?!\d)([\w$]+)$'
$rxHashed = [regex]'^(?!\d)([\w$]+)\.(?!\d)([\w$]+)\[([-a-zA-Z0-9+/*._&<> ]+)\]+$'
$rxIndexed = [regex]'^(?!\d)([\w$]+)\.(?!\d)([\w$]+):([0-9]+)$'
$rxHashedIndexed = [regex]'^(?!\d)([\w$]+)\.(?!\d)([\w$]+)\[([-a-zA-Z0-9+/*._&<> ]+)\]:\d+$'

$groups = @{}
$typeCounts = [ordered]@{ Single = 0; Hashed = 0; Indexed = 0; HashedIndexed = 0; Unparsed = 0 }
$unparsed = New-Object System.Collections.Generic.List[string]
$total = 0

foreach ($p in $entries.PSObject.Properties) {
  $key = $p.Name
  $total++

  $type = $null
  $g = $null
  $id = $null
  foreach ($pair in @(@('HashedIndexed', $rxHashedIndexed), @('Hashed', $rxHashed), @('Indexed', $rxIndexed), @('Single', $rxSingle))) {
    $m = $pair[1].Match($key)
    if ($m.Success) { $type = $pair[0]; $g = $m.Groups[1].Value; $id = $m.Groups[2].Value; break }
  }

  if (-not $type) {
    $type = 'Unparsed'
    $unparsed.Add($key)
    $dot = $key.IndexOf('.')
    if ($dot -gt 0) { $g = $key.Substring(0, $dot); $id = $key.Substring($dot + 1) } else { $g = '<no-dot>'; $id = $key }
  }
  $typeCounts[$type]++

  if (-not $groups.ContainsKey($g)) {
    $groups[$g] = [pscustomobject]@{ Group = $g; Entries = 0; Ids = (New-Object System.Collections.Generic.HashSet[string]) }
  }
  $groups[$g].Entries++
  [void]$groups[$g].Ids.Add($id)
}

$rows = $groups.Values | ForEach-Object {
  [pscustomobject]@{ Group = $_.Group; Ids = $_.Ids.Count; Entries = $_.Entries }
} | Sort-Object Group

Write-Output "total entries : $total"
Write-Output "groups        : $($rows.Count)"
Write-Output "distinct ids  : $(($rows | Measure-Object -Property Ids -Sum).Sum)"
Write-Output "by identifier type:"
$typeCounts.GetEnumerator() | ForEach-Object { Write-Output ("  {0,-14} {1}" -f $_.Key, $_.Value) }
if ($unparsed.Count -gt 0) {
  Write-Output "unparsed sample:"
  $unparsed | Select-Object -First 20 | ForEach-Object { Write-Output "  $_" }
}
Write-Output ""
$rows | Format-Table -AutoSize

if ($OutCsv) {
  $rows | Export-Csv -Path $OutCsv -NoTypeInformation -Encoding UTF8
  Write-Output "wrote: $OutCsv"
}
