using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
  options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton<OpenRct3AutomationSession>();
builder.Services.AddSingleton<OpenRct3Tools>();
builder.Services.AddSingleton<RetailRct3Session>();
builder.Services.AddSingleton<RetailRct3Tools>();
builder.Services
  .AddMcpServer()
  .WithStdioServerTransport()
  .WithToolsFromAssembly();
await builder.Build().RunAsync();
