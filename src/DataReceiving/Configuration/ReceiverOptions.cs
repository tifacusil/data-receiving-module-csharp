namespace DataReceiving.Configuration;

public class ReceiverOptions
{
    public int Port { get; set; } = 5000;
    public string DlxPath { get; set; } = "./dlx";
    public string OutputMode { get; set; } = "stdout";
    public string OutputFilePath { get; set; } = "./output";
    public RedisOptions Redis { get; set; } = new();
    public RabbitMqOptions RabbitMq { get; set; } = new();
    public ConsulOptions Consul { get; set; } = new();
}

public class RedisOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public int BufferTtlHours { get; set; } = 24;
    public int RetryIntervalSeconds { get; set; } = 30;
    public int RetryMinAgeMinutes { get; set; } = 2;
}

public class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string Username { get; set; } = "data_receiving";
    public string Password { get; set; } = "";
    public string Exchange { get; set; } = "sensors.raw";
    public int ConfirmTimeoutSeconds { get; set; } = 5;
}

public class ConsulOptions
{
    public string Address { get; set; } = "http://localhost:8500";
    public string Token { get; set; } = "";
    public string InstanceId { get; set; } = "data-receiving-01";
    public int HealthPort { get; set; } = 8080;
    public string ServiceVersion { get; set; } = "1.0.0";
    public string Environment { get; set; } = "prod";
}
