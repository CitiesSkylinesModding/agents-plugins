using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Demo MCP server entry point: a generic-host app speaking MCP over stdio.
var builder = Host.CreateApplicationBuilder(args);

// stdout carries the JSON-RPC protocol, so every log line must go to stderr instead; otherwise
// the framing breaks and the client fails the handshake.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();

await builder.Build().RunAsync();
