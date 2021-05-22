using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace MacSwitcher
{
    static class Program
    {
        static readonly string TargetIF = "en0";
        static readonly TimeSpan TestCycleInterval = TimeSpan.FromMinutes(10);
        static readonly TimeSpan RecheckBeforeChangedInternal = TimeSpan.FromSeconds(30);
        static readonly TimeSpan RecheckAfterChangedInternal = TimeSpan.FromSeconds(30);
        static readonly TimeSpan AppendAfterChangedInternal = TimeSpan.FromSeconds(10);
        static readonly TimeSpan MaxRecheckAfterChangedInternal = TimeSpan.FromMinutes(1);
        static readonly int TestNetworkTimeoutMs = (int)TimeSpan.FromSeconds(6).TotalMilliseconds;

        static async Task Main(string[] args)
        {
            await Task.Yield();

            var mac = NetworkInterface.GetAllNetworkInterfaces()
                .First(nif => nif.Name.Equals(TargetIF, StringComparison.Ordinal))
                .GetPhysicalAddress().GetAddressBytes();
            Log("current '{0}' mac '{1}'", TargetIF, mac.ToMacFormat());

            var test = await RunCommandAsync("/sbin/ifconfig", TargetIF + " ether");
            Log("test ifconfig cmd: {0}", test);

            while (true)
            {
                await Task.Delay(TestCycleInterval);

                if (await TestNetwork()) continue;

                Log("Wait {0}s to check again ...", RecheckBeforeChangedInternal.TotalSeconds);
                await Task.Delay(RecheckBeforeChangedInternal);

                var changedDelay = RecheckAfterChangedInternal;
                while (!await TestNetwork())
                {
                    var current = mac.ToMacFormat();
                    mac[mac.Length - 1]--;
                    var target = mac.ToMacFormat();
                    var result = await RunCommandAsync("/sbin/ifconfig", TargetIF + " ether " + target);
                    Log("ifconfig cmd ch mac '{0}' -> '{1}': {2}", current, target, result);

                    await Task.Delay(changedDelay);

                    if (changedDelay < MaxRecheckAfterChangedInternal)
                        changedDelay += AppendAfterChangedInternal;
                }
            }
        }

        public static async Task<int> RunCommandAsync(string command, string args)
        {
            Log("RunCommandAsync: \"{0}\" {1}", command, args);
            var info = new ProcessStartInfo(command, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                StandardOutputEncoding = Encoding.UTF8,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using var process = Process.Start(info);
            do await Task.Yield(); while (!process.WaitForExit(33));
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(process.StandardOutput.ReadToEnd());
            stringBuilder.AppendLine(process.StandardError.ReadToEnd());
            Log(stringBuilder.ToString());
            return process.ExitCode;
        }

        static void Log(string format, params object[] args)
            => Console.WriteLine("{0:yyyy/MM/dd HH:mm:ss}: {1}", DateTime.Now, args?.Any() ?? false ? string.Format(format, args) : format);

        static string ToMacFormat(this byte[] bs)
            => bs.Any() ? bs.Aggregate(Builder.Clear(), (sb, b) => sb.AppendFormat(":{0:x02}", b)).Remove(0, 1).ToString() : "";


        [ThreadStatic]
        static readonly StringBuilder Builder = new StringBuilder(17);

        static readonly IReadOnlyList<string> TestTargets = new[]
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
            "https://www.youtube.com/",
        };

        static readonly Random Random = new Random();

        static string FetchTestTarget() => TestTargets[Random.Next(TestTargets.Count)];

        static async Task<bool> TestNetwork()
        {
            var target = FetchTestTarget();
            try
            {
                var request = WebRequest.CreateHttp(target);
                request.Timeout = request.ReadWriteTimeout = TestNetworkTimeoutMs;
                using var response = await request.GetResponseAsync();
                using var stream = response.GetResponseStream();
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(content))
                    throw new Exception("Context empty");
                Log("TestNetwork Succeed '{0}'", target);
                return true;
            }
            catch (Exception ex)
            {
                Log("TestNetwork Fault '{0}': {1}", target, ex.Message);
                return false;
            }
        }
    }
}
