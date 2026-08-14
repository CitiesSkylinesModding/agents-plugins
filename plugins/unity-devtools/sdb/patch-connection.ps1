#!/usr/bin/env pwsh
# Build-time patches for the vendored Mono.Debugger.Soft Connection.cs, applied before compiling.
# The result is emitted into obj/ so the vendored tree stays pristine (invoked from the
# PatchVendoredConnection target in UnityDevtools.Sdb.csproj).
#
# 1. Its BeginInvoke reply dispatch throws on modern .NET, so the one offending call is rewritten
#    into a Task.Run.
# 2. Its receiver thread reports a failed receive on stdout. The host process serves MCP over
#    stdio, where stdout carries JSON-RPC and nothing else, so a debuggee that dies abruptly would
#    write a stack trace into the protocol stream and break the session. The diagnostic is worth
#    keeping, so it moves to stderr rather than being suppressed.
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

$patches = @(
  @{
    Anchor = 'cb.BeginInvoke (r, null, null)'
    Replacement = 'System.Threading.Tasks.Task.Run (() => cb (r))'
  },
  @{
    Anchor = 'Console.WriteLine (ex);'
    Replacement = 'Console.Error.WriteLine (ex);'
  }
)

foreach ($patch in $patches)
{
  if (-not $src.Contains($patch.Anchor))
  {
    Write-Error (
      "vendored Connection.cs patch anchor missing (upstream changed?): " + $patch.Anchor
    )

    exit 1
  }

  $src = $src.Replace($patch.Anchor, $patch.Replacement)
}

$src | Set-Content -NoNewline $Output
