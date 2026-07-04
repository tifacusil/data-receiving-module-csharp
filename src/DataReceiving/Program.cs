using Consul;
using DataReceiving.Buffer;
using DataReceiving.Configuration;
using DataReceiving.DeadLetter;
using DataReceiving.Messaging;
using DataReceiving.Networking;
using DataReceiving.Observability;
using DataReceiving.Parsing;
using DataReceiving.ServiceDiscovery;
using DataReceiving.Sinks;
using DataReceiving.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using StackExchange.Redis;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<ReceiverOptions>(ctx.Configuration.GetSection("Receiver"));

        // Métricas del pipeline: se recolectan siempre (cualquier OutputMode);
        // solo se exponen en /metrics cuando el HealthEndpoint corre (modo rabbitmq).
        services.AddSingleton<PipelineMetrics>();

        services.AddSingleton<SchemaValidator>();
        services.AddSingleton<IDtuPacketParser, DtuPacketParser>();

        services.AddSingleton<LocalDlxWriter>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ReceiverOptions>>().Value;
            return new LocalDlxWriter(opts.DlxPath);
        });

        var outputMode = ctx.Configuration["Receiver:OutputMode"] ?? "stdout";

        services.AddSingleton<TcpServerStatus>();

        if (outputMode == "rabbitmq")
        {
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<ReceiverOptions>>().Value.Redis;
                return ConnectionMultiplexer.Connect(opts.ConnectionString);
            });
            services.AddSingleton<IMessageBuffer, RedisMessageBuffer>();

            services.AddSingleton<IConnection>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<ReceiverOptions>>().Value.RabbitMq;
                var factory = new ConnectionFactory
                {
                    HostName = opts.Host,
                    Port = opts.Port,
                    VirtualHost = opts.VirtualHost,
                    UserName = opts.Username,
                    Password = opts.Password,
                    AutomaticRecoveryEnabled = true,
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                };
                return factory.CreateConnection("data-receiving");
            });
            services.AddSingleton<IModel>(sp =>
            {
                var conn = sp.GetRequiredService<IConnection>();
                var channel = conn.CreateModel();
                channel.ConfirmSelect();
                return channel;
            });
            services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
            services.AddSingleton<RabbitMqSink>();
            services.AddHostedService<RetryWorker>();

            services.AddSingleton<IConsulClient>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<ReceiverOptions>>().Value.Consul;
                return new ConsulClient(cfg =>
                {
                    cfg.Address = new Uri(opts.Address);
                    if (!string.IsNullOrEmpty(opts.Token))
                        cfg.Token = opts.Token;
                });
            });
            services.AddHostedService<HealthEndpoint>();
            services.AddHostedService<ConsulRegistration>();
        }

        services.AddSingleton<StdoutSink>();
        services.AddSingleton<FileSink>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ReceiverOptions>>().Value;
            return new FileSink(opts.OutputFilePath);
        });
        services.AddSingleton<NullSink>();

        services.AddSingleton<ISink>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ReceiverOptions>>().Value;
            return opts.OutputMode switch
            {
                "rabbitmq" => (ISink)sp.GetRequiredService<RabbitMqSink>(),
                "file"     => sp.GetRequiredService<FileSink>(),
                "null"     => sp.GetRequiredService<NullSink>(),
                _          => sp.GetRequiredService<StdoutSink>(),
            };
        });

        services.AddSingleton<TcpServer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<ReceiverOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<TcpServer>>();
            var parser = sp.GetRequiredService<IDtuPacketParser>();
            var sink = sp.GetRequiredService<ISink>();
            var dlx = sp.GetRequiredService<LocalDlxWriter>();
            var metrics = sp.GetRequiredService<PipelineMetrics>();
            return new TcpServer(opts.Port, parser, sink, dlx, logger, metrics);
        });
        services.AddHostedService(sp => new TcpServerHostedService(
            sp.GetRequiredService<TcpServer>(),
            sp.GetRequiredService<TcpServerStatus>()));
    })
    .Build();

await host.RunAsync();
