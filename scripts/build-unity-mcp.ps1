# Publishes the unity-devtools MCP server as the committed single-file exe in mcp/dist/.
#
# Windows locks a running exe against overwrite but not against rename, so when a live MCP server
# still serves the old build, the exe is renamed aside (*.stale*, gitignored) and a fresh one is
# published in its place; reconnecting via /mcp then picks up the new file.
# Stale copies are cleaned up on the next run once the old server has exited (a copy still held by a
# running server gets a unique timestamped name instead).

$ErrorActionPreference = 'Stop'

$dist = Join-Path $PSScriptRoot '../plugins/unity-devtools/mcp/dist'
$exe = Join-Path $dist 'unity-devtools-mcp.exe'

Get-ChildItem -Path $dist -Filter '*.stale*' -ErrorAction SilentlyContinue | ForEach-Object {
  try {
    Remove-Item $_.FullName -Force
  } catch {
    # Still locked by a not-yet-restarted server; a later run will collect it.
  }
}

$stale = $null

if (Test-Path $exe) {
  $stale = "$exe.stale"

  if (Test-Path $stale) {
    $stale = "$exe.stale-$(Get-Date -Format yyyyMMddHHmmssfff)"
  }

  Move-Item $exe $stale -Force
}

# Framework-dependent single-file exe: one launchable file in dist/, committed (zero-build for
# plugin installs; users need the .NET 10 runtime). '-r' is required for PublishSingleFile,
# win-x64 matches the Windows-only port discovery. DebugType=None keeps the pdb out of dist/.
dotnet publish (Join-Path $PSScriptRoot '../plugins/unity-devtools/mcp/UnityDevtools.Mcp.csproj') `
  -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=None -o $dist

# A failed publish must not leave dist/ without its committed exe: restore the renamed one.
if (($LASTEXITCODE -ne 0) -and (-not (Test-Path $exe)) -and $stale -and (Test-Path $stale)) {
  Move-Item $stale $exe -Force
}

exit $LASTEXITCODE
