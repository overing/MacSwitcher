

await new HostBuilder()
    .UseContentRoot(Environment.CurrentDirectory)
    .ConfigureAppConfiguration((context, configuration) =>
    {
        configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
    })
    .ConfigureLogging((context, logging) =>
    {
        logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "[yyyy/MM/dd HH:mm:ss.ff] ";
        });
        logging.AddConfiguration(context.Configuration.GetSection("Logging"));
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<MacSwitcherOptiions>(context.Configuration.GetSection("Options"));
        services.AddHostedService<MacSwitcher>();
    })
    .RunConsoleAsync();

public sealed class MacSwitcherOptiions
{
    public string TargetIF { get; set; } = "en0";

    public TimeSpan TestCycleInterval { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan RecheckBeforeChangedInternal { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RecheckAfterChangedInternal { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan AppendAfterChangedInternal { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan MaxRecheckAfterChangedInternal { get; set; } = TimeSpan.FromMinutes(1);

    public HashSet<string> TestTargets { get; set; } = new();

    public static readonly IReadOnlyCollection<string> DefaultTestTargets = new[]
    {
        "https://discord.com/",
        "https://twitter.com/",
        "https://www.amazon.com/",
        "https://www.apple.com/",
        "https://www.bing.com/",
        "https://www.facebook.com/",
        "https://www.google.com/",
        "https://www.instagram.com/",
        "https://www.linkedin.com/",
        "https://www.netflix.com/tw/",
        "https://www.microsoft.com/",
        "https://www.pinterest.com/",
        "https://www.reddit.com/",
        "https://www.twitch.tv/",
        "https://www.wikipedia.org/",
        "https://www.yahoo.com/",
        "https://www.youtube.com/"
    };
}

public sealed class MacSwitcher : BackgroundService
{
    readonly ILogger Logger;
    readonly IOptionsMonitor<MacSwitcherOptiions> Monitor;

    readonly StringBuilder Builder = new(capacity: 17);
    readonly Random Random = new();
    readonly HttpClient HttpClient = new();

    public MacSwitcher(ILogger<MacSwitcher> logger, IOptionsMonitor<MacSwitcherOptiions> monitor)
    {
        Logger = logger;
        Monitor = monitor;
    }

    string ToMacFormat(byte[] bs)
        => bs.Any() ? bs.Aggregate(Builder.Clear(), (sb, b) => sb.AppendFormat(":{0:x02}", b)).Remove(0, 1).ToString() : "";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var options = Monitor.CurrentValue;
        var ifName = options.TargetIF;
        var mac = NetworkInterface.GetAllNetworkInterfaces()
            .First(nif => nif.Name.Equals(ifName, StringComparison.Ordinal))
            .GetPhysicalAddress().GetAddressBytes();
        Logger.LogInformation("current '{0}' mac '{1}'", ifName, ToMacFormat(mac));

        var test = await RunCommandAsync("/sbin/ifconfig", ifName + " ether");
        Logger.LogInformation("test ifconfig cmd: {0}", test);

        while (true)
        {
            var targets = options.TestTargets is { Count: > 0 } ts ? ts : MacSwitcherOptiions.DefaultTestTargets;
            if (!await TestNetwork(targets))
            {
                var afterChangedInterval = options.RecheckBeforeChangedInternal;
                Logger.LogInformation("Wait {0}s to check again ...", afterChangedInterval.TotalSeconds);
                await Task.Delay(afterChangedInterval);

                var changedDelay = options.RecheckAfterChangedInternal;
                while (!await TestNetwork(targets))
                {
                    var current = ToMacFormat(mac);
                    mac[mac.Length - 1]--;
                    var target = ToMacFormat(mac);
                    var result = await RunCommandAsync("/sbin/ifconfig", options.TargetIF + " ether " + target);
                    Logger.LogWarning("ifconfig cmd ch mac '{0}' -> '{1}': {2}", current, target, result);

                    await Task.Delay(changedDelay);

                    if (changedDelay < options.MaxRecheckAfterChangedInternal)
                        changedDelay += options.AppendAfterChangedInternal;
                }
            }

            await Task.Delay(options.TestCycleInterval);
            options = Monitor.CurrentValue;
        }
    }

    async Task<bool> TestNetwork(IReadOnlyCollection<string> targets)
    {
        var target = targets.ElementAt(Random.Next(targets.Count));
        try
        {
            var content = await HttpClient.GetStringAsync(target);
            if (string.IsNullOrWhiteSpace(content))
                throw new Exception("Context empty");
            Logger.LogInformation("TestNetwork Succeed '{0}'", target);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning("TestNetwork Fault '{0}': {1}", target, ex.Message);
            return false;
        }
    }

    public async Task<int> RunCommandAsync(string command, string args)
    {
        Logger.LogInformation("RunCommandAsync: \"{0}\" {1}", command, args);
        var info = new ProcessStartInfo(command, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            StandardOutputEncoding = Encoding.UTF8,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        using var process = Process.Start(info)!;
        do await Task.Yield(); while (!process.WaitForExit(33));
        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine(process.StandardOutput.ReadToEnd());
        stringBuilder.AppendLine(process.StandardError.ReadToEnd());
        Logger.LogInformation(stringBuilder.ToString());
        return process.ExitCode;
    }
}
