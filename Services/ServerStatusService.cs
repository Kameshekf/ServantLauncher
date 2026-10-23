using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ServantLauncher.Services
{
    public class ServerPingInfo
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 25565;
        public bool IsOnline { get; set; }
        public long PingMs { get; set; }
        public string StatusText => IsOnline ? $"{PingMs} ms" : "Офлайн";
    }

    public interface IServerStatusService
    {
        Task<ServerPingInfo> PingServerAsync(string host, int port = 25565, int timeoutMs = 3000);
    }

    public class ServerStatusService : IServerStatusService
    {
        public async Task<ServerPingInfo> PingServerAsync(string host, int port = 25565, int timeoutMs = 3000)
        {
            var info = new ServerPingInfo { Host = host, Port = port };
            try
            {
                using var client = new TcpClient();
                var sw = Stopwatch.StartNew();
                var connectTask = client.ConnectAsync(host, port);
                var delayTask = Task.Delay(timeoutMs);

                var completed = await Task.WhenAny(connectTask, delayTask);
                sw.Stop();

                if (completed == connectTask && client.Connected)
                {
                    info.IsOnline = true;
                    info.PingMs = Math.Max(1, sw.ElapsedMilliseconds);
                }
            }
            catch
            {
                info.IsOnline = false;
            }
            return info;
        }
    }
}
