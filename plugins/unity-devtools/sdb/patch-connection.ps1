#!/usr/bin/env pwsh
# Build-time patch for the vendored Mono.Debugger.Soft Connection.cs: its BeginInvoke reply
# dispatch throws on modern .NET, so the one offending call is rewritten into a Task.Run before
# compiling. The result is emitted into obj/ so the vendored tree stays pristine (invoked from the
# PatchVendoredConnection target in UnityDevtools.Sdb.csproj).
#
# This lives in a script file rather than inline in <Exec> on purpose: MSBuild runs Exec commands
# through /bin/sh on Linux, which would expand the $src/$anchor PowerShell variables (to empty)
# before pwsh ever parsed the command. A script file keeps those variables out of the shell's reach,
# so the target builds identically on Windows and Linux.
param(
    [Parameter(Mandatory)] [string] $Source,
    [Parameter(Mandatory)] [string] $Output
)

$ErrorActionPreference = 'Stop'

$src = Get-Content -Raw $Source
$anchor = 'cb.BeginInvoke (r, null, null)'

if (-not $src.Contains($anchor)) {
    Write-Error 'vendored Connection.cs patch anchor missing (upstream changed?)'
    exit 1
}

$src.Replace($anchor, 'System.Threading.Tasks.Task.Run (() => cb (r))') |
    Set-Content -NoNewline $Output
