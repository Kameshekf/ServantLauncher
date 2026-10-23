using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ServantLauncher.Services
{
    public interface IDiscordRpcService
    {
        void Initialize();
        void SetPresence(string details, string state, bool isPlaying = false);
        void ClearPresence();
        void SetEnabled(bool enabled);
        void UpdateConfig(bool enabled, string? customClientId, bool showPage, bool showProfile, bool showTime);
        void Shutdown();
    }

    public class DiscordRpcService : IDiscordRpcService
    {
        // Hardcoded Discord Application ID for Servant Launcher
        private const string DefaultClientId = "1508502396899426354";
        private const string ServerIconAssetKey = "server-icon.png";

        private string _activeClientId = DefaultClientId;
        private NamedPipeClientStream? _pipeStream;
        private bool _isConnected;
        private bool _isEnabled = true;
        private bool _showPage = true;
        private bool _showProfile = true;
        private bool _showTime = true;
        private readonly long _launcherStartTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        private long _gameStartTimestamp = 0;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private string _lastDetails = "Servant Launcher BETA";
        private string _lastState = "В лаунчере";
        private bool _lastIsPlaying = false;
        private CancellationTokenSource? _reconnectCts;

        public DiscordRpcService(bool isEnabled = true, string? customClientId = null, bool showPage = true, bool showProfile = true, bool showTime = true)
        {
            _isEnabled = isEnabled;
            _activeClientId = DefaultClientId;
            _showPage = showPage;
            _showProfile = showProfile;
            _showTime = showTime;
        }

        public void Initialize()
        {
            if (!_isEnabled) return;
            SetPresence("Servant Launcher BETA", "В лаунчере", false);
            StartReconnectLoop();
        }

        private void StartReconnectLoop()
        {
            _reconnectCts?.Cancel();
            _reconnectCts = new CancellationTokenSource();
            var token = _reconnectCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(8000, token).ConfigureAwait(false);
                        if (_isEnabled && !_isConnected)
                        {
                            await _lock.WaitAsync(token).ConfigureAwait(false);
                            try
                            {
                                if (!_isConnected)
                                {
                                    if (await TryConnectAsync().ConfigureAwait(false))
                                    {
                                        await SendActivityAsync(_lastDetails, _lastState, _lastIsPlaying).ConfigureAwait(false);
                                    }
                                }
                            }
                            catch { }
                            finally
                            {
                                _lock.Release();
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch { }
                }
            }, token);
        }

        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
            if (!enabled)
            {
                ClearPresence();
            }
            else
            {
                SetPresence(_lastDetails, _lastState, _lastIsPlaying);
            }
        }

        public void UpdateConfig(bool enabled, string? customClientId, bool showPage, bool showProfile, bool showTime)
        {
            var newId = !string.IsNullOrWhiteSpace(customClientId) ? customClientId.Trim() : DefaultClientId;
            bool idChanged = !string.Equals(_activeClientId, newId, StringComparison.OrdinalIgnoreCase);

            _isEnabled = enabled;
            _activeClientId = newId;
            _showPage = showPage;
            _showProfile = showProfile;
            _showTime = showTime;

            if (!enabled)
            {
                ClearPresence();
            }
            else
            {
                if (idChanged)
                {
                    CloseConnection();
                }
                SetPresence(_lastDetails, _lastState, _lastIsPlaying);
            }
        }

        public void SetPresence(string details, string state, bool isPlaying = false)
        {
            _lastDetails = details;
            _lastState = state;
            _lastIsPlaying = isPlaying;

            if (isPlaying && _gameStartTimestamp == 0)
            {
                _gameStartTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            else if (!isPlaying)
            {
                _gameStartTimestamp = 0;
            }

            if (!_isEnabled) return;

            _ = Task.Run(async () =>
            {
                await _lock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!_isConnected || _pipeStream == null || !_pipeStream.IsConnected)
                    {
                        await TryConnectAsync().ConfigureAwait(false);
                    }

                    if (_isConnected && _pipeStream != null && _pipeStream.IsConnected)
                    {
                        await SendActivityAsync(details, state, isPlaying).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Discord RPC] Error: {ex.Message}");
                    CloseConnection();
                }
                finally
                {
                    _lock.Release();
                }
            });
        }

        public void ClearPresence()
        {
            _ = Task.Run(async () =>
            {
                await _lock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_isConnected && _pipeStream != null && _pipeStream.IsConnected)
                    {
                        await SendClearActivityAsync().ConfigureAwait(false);
                    }
                }
                catch { }
                finally
                {
                    _lock.Release();
                }
            });
        }

        private async Task<bool> TryConnectAsync()
        {
            CloseConnection();

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                    await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
                    _pipeStream = pipe;

                    // Send Handshake (Opcode 0)
                    var handshakeObj = new { v = 1, client_id = _activeClientId };
                    var json = JsonSerializer.Serialize(handshakeObj);
                    await WriteFrameAsync(0, json).ConfigureAwait(false);

                    // Read Handshake response with timeout
                    var (op, resp) = await ReadFrameWithTimeoutAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    if (op == 1) // Frame received
                    {
                        _isConnected = true;
                        Debug.WriteLine($"[Discord RPC] Connected successfully on pipe {i}");
                        return true;
                    }
                    else
                    {
                        Debug.WriteLine($"[Discord RPC] Handshake rejected on pipe {i}: op={op}");
                        CloseConnection();
                    }
                }
                catch
                {
                    CloseConnection();
                }
            }

            return false;
        }

        private async Task SendActivityAsync(string details, string state, bool isPlaying)
        {
            var pid = Process.GetCurrentProcess().Id;
            long startTs = isPlaying && _gameStartTimestamp > 0 ? _gameStartTimestamp : _launcherStartTimestamp;

            string displayDetails = details;
            string displayState = state;

            if (!isPlaying && !_showPage)
            {
                displayDetails = "Servant Launcher BETA";
                displayState = "В лаунчере";
            }
            else if (isPlaying && !_showProfile)
            {
                displayDetails = "В игре Minecraft";
                displayState = "Играет на сервере";
            }

            object? timestampsObj = _showTime ? new { start = startTs } : null;

            var payload = new
            {
                cmd = "SET_ACTIVITY",
                args = new
                {
                    pid = pid,
                    activity = new
                    {
                        details = displayDetails,
                        state = displayState,
                        assets = new
                        {
                            large_image = ServerIconAssetKey,
                            large_text = "Servant Launcher",
                            small_image = isPlaying ? ServerIconAssetKey : ServerIconAssetKey,
                            small_text = isPlaying ? "В игре Minecraft" : "Servant Launcher"
                        },
                        timestamps = timestampsObj
                    }
                },
                nonce = Guid.NewGuid().ToString("N")
            };

            var json = JsonSerializer.Serialize(payload);
            await WriteFrameAsync(1, json).ConfigureAwait(false);

            // Read response frame
            _ = await ReadFrameWithTimeoutAsync(TimeSpan.FromMilliseconds(1500)).ConfigureAwait(false);
        }

        private async Task SendClearActivityAsync()
        {
            var pid = Process.GetCurrentProcess().Id;
            var payload = new
            {
                cmd = "SET_ACTIVITY",
                args = new
                {
                    pid = pid,
                    activity = (object?)null
                },
                nonce = Guid.NewGuid().ToString("N")
            };

            var json = JsonSerializer.Serialize(payload);
            await WriteFrameAsync(1, json).ConfigureAwait(false);
        }

        private async Task WriteFrameAsync(int opcode, string json)
        {
            if (_pipeStream == null || !_pipeStream.IsConnected) return;

            var bytes = Encoding.UTF8.GetBytes(json);
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(opcode);
            writer.Write(bytes.Length);
            writer.Write(bytes);

            var packet = ms.ToArray();
            await _pipeStream.WriteAsync(packet, 0, packet.Length).ConfigureAwait(false);
            await _pipeStream.FlushAsync().ConfigureAwait(false);
        }

        private async Task<(int Opcode, string Json)> ReadFrameWithTimeoutAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                return await ReadFrameAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
                return (-1, string.Empty);
            }
        }

        private async Task<(int Opcode, string Json)> ReadFrameAsync(CancellationToken ct = default)
        {
            if (_pipeStream == null || !_pipeStream.IsConnected) return (-1, string.Empty);

            var header = new byte[8];
            int read = 0;
            while (read < 8)
            {
                int r = await _pipeStream.ReadAsync(header.AsMemory(read, 8 - read), ct).ConfigureAwait(false);
                if (r <= 0) return (-1, string.Empty);
                read += r;
            }

            int opcode = BitConverter.ToInt32(header, 0);
            int length = BitConverter.ToInt32(header, 4);

            if (length <= 0) return (opcode, string.Empty);

            var buffer = new byte[length];
            int totalRead = 0;
            while (totalRead < length)
            {
                int r = await _pipeStream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead), ct).ConfigureAwait(false);
                if (r <= 0) break;
                totalRead += r;
            }

            var json = Encoding.UTF8.GetString(buffer, 0, totalRead);
            return (opcode, json);
        }

        private void CloseConnection()
        {
            _isConnected = false;
            try
            {
                _pipeStream?.Dispose();
            }
            catch { }
            _pipeStream = null;
        }

        public void Shutdown()
        {
            _reconnectCts?.Cancel();
            ClearPresence();
            CloseConnection();
        }
    }
}
