using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Net;

var configuration = LoadConfiguration(args);
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .CreateLogger();

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Information).AddSerilog();
});
var applogger = loggerFactory.CreateLogger<Program>();
Console.OutputEncoding = System.Text.Encoding.UTF8;

var clientList = new List<TestClient>();
try
{
    var config = new AppConfig(configuration, loggerFactory.CreateLogger<AppConfig>());

    applogger.LogInformation("🚀 程式開始執行");

    NetworkHelper.MonitorNetworkChanges(IPAddress.Parse(config.IP), applogger);


    var connectionSettings = configuration.GetSection("ConnectionSetting").Get<List<ConnectionSetting>>();

    foreach (var setting in connectionSettings)
    {
        var logger = loggerFactory.CreateLogger<TestClient>();

        var client = new TestClient(setting.IP, setting.Port, logger)
        {
            EnableConnectionRetry = setting.ConnectionRetry.Enable,
            EnableCheckConnection = setting.CheckConnection.Enable,
            EnableTcpKeepAlive = setting.TcpKeepAlive.Enable,
            ConnectionRetryInterval = setting.ConnectionRetry.Interval,
            CheckConnectionInterval = setting.CheckConnection.Interval,
            TCPKeepAliveInterval = setting.TcpKeepAlive.Interval,
            TCPKeepAliveTime = setting.TcpKeepAlive.Time,
            Name = setting.Name
        };


        clientList.Add(client);
    }


    var tasks = clientList.Select(client => client.StartAsync()).ToArray();
    Task.WaitAll(tasks);

    applogger.LogInformation("🛑 程式結束執行");
}
catch (Exception ex)
{
    applogger.LogError(ex, "❌ 程式發生例外");
}

Log.CloseAndFlush();

static IConfiguration LoadConfiguration(string[] args)
{
    return new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .Build();
}

AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    Log.Logger.Error("Unhandled exception: {Exception}", e.ExceptionObject.ToString());
    Log.CloseAndFlush();
};