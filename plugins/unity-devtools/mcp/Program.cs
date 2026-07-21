using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnityDevtools.Mcp;
using UnityDevtools.Sdb;
using UnityDevtools.Sdb.Eval;

// Error messages travel to coding agents; keep framework diagnostics (e.g., Roslyn parse errors in
// eval) in English regardless of the host machine's locale.
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

// MCP server entry point: a generic-host app speaking MCP over stdio.
var builder = Host.CreateApplicationBuilder(args);

// stdout carries the JSON-RPC protocol, so every log line must go to stderr instead; otherwise
// the framing breaks and the client fails the handshake.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// One persistent debugger session shared by every tool, configured from the environment (all
// optional; with nothing set, the game is auto-discovered by its SDB port signature).
// Registered through a factory, so the container owns it and disposes it on shutdown, which resumes
// the game and frees the exclusive debugger slot.
builder.Services.AddSingleton(_ => new UnitySession(
    new UnitySessionConfig {
      Host = Env("UNITY_MCP_HOST"),
      Port = ParsePort(Env("UNITY_MCP_PORT")),
      ProcessNamePrefix = Env("UNITY_MCP_PROCESS")
    }
  )
);

// The eval tool's `_` last-result slot lives for the whole server session.
builder.Services.AddSingleton<EvalState>();

// Dies with the launching wrapper AND with its own stdin, so an MCP reconnection never strands a
// stale server holding the exclusive SDB slot (see ParentWatchdog and StdinWatchdog); belt and
// braces on independent signals.
builder.Services.AddHostedService<ParentWatchdog>();
builder.Services.AddHostedService<StdinWatchdog>();

builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();

var host = builder.Build();

// EVERY stop gets the hard-exit failsafe, whoever triggered it (the transport's own stdin-EOF
// handling included): graceful shutdown disposes the SDB session, which can stall forever against
// an unresponsive debuggee (see HardExit).
host.Services.GetRequiredService<IHostApplicationLifetime>()
  .ApplicationStopping.Register(HardExit.Arm);

await host.RunAsync();

return;

// Plugin harnesses pass declared-but-unset env vars through as empty strings; treat those as unset.
static string? Env(string name) {
  var value = Environment.GetEnvironmentVariable(name);

  return string.IsNullOrEmpty(value) ? null : value;
}

// A malformed pin must fail loudly: silently falling back to auto-discovery could attach the
// session (and its writes) to a different game than the one the user pinned.
static int? ParsePort(string? raw) {
  return raw is null
    ? null
    : int.TryParse(raw, out var port)
      ? port
      : throw new InvalidOperationException($"UNITY_MCP_PORT is not a valid port: '{raw}'");
}
