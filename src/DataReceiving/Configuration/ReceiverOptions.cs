namespace DataReceiving.Configuration;

public class ReceiverOptions
{
    public int Port { get; set; } = 5000;
    public string DlxPath { get; set; } = "./dlx";
    public string OutputMode { get; set; } = "stdout";
    public string OutputFilePath { get; set; } = "./output";
}
