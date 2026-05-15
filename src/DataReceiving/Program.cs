using DataReceiving.Configuration;
using DataReceiving.DeadLetter;
using DataReceiving.Networking;
using DataReceiving.Parsing;
using DataReceiving.Sinks;
using DataReceiving.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables(prefix: "RECEIVER__")
    .Build();

var services = new ServiceCollection();
services.Configure<ReceiverOptions>(config.GetSection("Receiver"));
services.AddLogging(b => b
    .AddConsole()
    .AddConfiguration(config.GetSection("Logging")));
services.AddSingleton<SchemaValidator>();
services.AddSingleton<IDtuPacketParser, DtuPacketParser>();

var provider = services.BuildServiceProvider();

var opts = provider.GetRequiredService<IOptions<ReceiverOptions>>().Value;
var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger<TcpServer>();
var parser = provider.GetRequiredService<IDtuPacketParser>();

ISink sink = opts.OutputMode switch
{
    "file" => new FileSink(opts.OutputFilePath),
    "null" => new NullSink(),
    _ => new StdoutSink(),
};

var dlx = new LocalDlxWriter(opts.DlxPath);
var server = new TcpServer(opts.Port, parser, sink, dlx, logger);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await server.RunAsync(cts.Token);
