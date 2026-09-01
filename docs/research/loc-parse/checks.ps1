param([Parameter(Mandatory = $true)][string]$Json)

$doc = [System.IO.File]::ReadAllText($Json) | ConvertFrom-Json
$entries = $doc.entries
$indexCounts = $doc.indexCounts

Write-Output "=== indexCounts ==="
$ic = $indexCounts.PSObject.Properties | ForEach-Object {
  [pscustomobject]@{ Key = $_.Name; Count = [int]$_.Value; Group = $_.Name.Split('.')[0] }
}
Write-Output ("declared index-count keys: " + $ic.Count)
Write-Output ("sum of variants: " + (($ic | Measure-Object Count -Sum).Sum))
Write-Output "per group:"
$ic | Group-Object Group | Sort-Object Name | ForEach-Object {
  "  {0,-20} {1,3} keys, max variants {2}" -f $_.Name, $_.Count, (($_.Group | Measure-Object Count -Maximum).Maximum)
}
Write-Output "largest index counts:"
$ic | Sort-Object Count -Descending | Select-Object -First 8 | ForEach-Object { "  {0,-46} {1}" -f $_.Key, $_.Count }

Write-Output ""
Write-Output "=== placeholders in values ==="
$plain = [regex]'\{(?!\d)([A-Z0-9_]+)\}'
$spec = [regex]'\{([A-Za-z0-9_]+):([A-Za-z]+)( signed)?\}'
$any = [regex]'\{([^{}]+)\}'
$nPlain = 0; $nSpec = 0; $nAny = 0
$units = @{}
$specSamples = New-Object System.Collections.Generic.List[string]
foreach ($p in $entries.PSObject.Properties) {
  $v = $p.Value
  if (-not $v) { continue }
  if ($any.IsMatch($v)) { $nAny++ }
  if ($plain.IsMatch($v)) { $nPlain++ }
  foreach ($m in $spec.Matches($v)) {
    $nSpec++
    $u = $m.Groups[2].Value
    if (-not $units.ContainsKey($u)) { $units[$u] = 0 }
    $units[$u]++
    if ($specSamples.Count -lt 10) { $specSamples.Add($p.Name + "  ->  " + $m.Value) }
  }
}
Write-Output ("values with any {..} token      : " + $nAny)
Write-Output ("values with a bare {UPPER_SNAKE}: " + $nPlain)
Write-Output ("occurrences of {NAME:Unit}      : " + $nSpec)
Write-Output "unit names used after the colon:"
$units.GetEnumerator() | Sort-Object -Property Value -Descending | ForEach-Object { "  {0,-24} {1}" -f $_.Key, $_.Value }
Write-Output "samples:"
$specSamples | ForEach-Object { "  $_" }

Write-Output ""
Write-Output "=== compiler fingerprints ==="
$lang = $entries.PSObject.Properties | Where-Object { $_.Name -like 'Options.LANGUAGE`[*' } | ForEach-Object { $_.Name }
Write-Output ("Options.LANGUAGE[..] keys: " + $lang.Count)
$lang | Sort-Object | ForEach-Object { "  $_" }
$old = $entries.PSObject.Properties | Where-Object { $_.Name -like 'old.*' }
Write-Output ("keys starting with 'old.': " + $old.Count)
