using NLog;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace adrilight.Util
{
    internal sealed class TcpControlServer : IDisposable
    {
        private static readonly ILogger _log = LogManager.GetCurrentClassLogger();

        private readonly IUserSettings _userSettings;
        private readonly IModeManager _modeManager;
        private readonly int _port;
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _listenerTask;

        public TcpControlServer(IUserSettings userSettings, IModeManager modeManager, int port = 5080)
        {
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            _modeManager = modeManager ?? throw new ArgumentNullException(nameof(modeManager));
            _port = port;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _listenerTask = Task.Run(() => ListenLoop(_cts.Token));
            _log.Info($"TcpControlServer started on port {_port}.");
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _log.Info("TcpControlServer stopped.");
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => HandleClient(client), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        _log.Warn(ex, "TcpControlServer error accepting client.");
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = 2000;
                    client.SendTimeout = 2000;

                    var stream = client.GetStream();
                    var buffer = new byte[64];
                    var bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) return;

                    var command = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim().ToUpperInvariant();
                    _log.Debug($"TcpControlServer received command: {command}");

                    string response;

                    switch (command)
                    {
                        case "ON":
                            _userSettings.TransferActive = true;
                            response = "{\"status\":\"on\"}";
                            break;

                        case "OFF":
                            _userSettings.TransferActive = false;
                            response = "{\"status\":\"off\"}";
                            break;

                        case "TOGGLE":
                            _userSettings.TransferActive = !_userSettings.TransferActive;
                            response = _userSettings.TransferActive
                                ? "{\"status\":\"on\"}"
                                : "{\"status\":\"off\"}";
                            break;

                        case "STATUS":
                            var statusMode = ModeToString(_modeManager.ActiveMode);
                            response = _userSettings.TransferActive
                                ? $"{{\"status\":\"on\",\"mode\":\"{statusMode}\"}}"
                                : $"{{\"status\":\"off\",\"mode\":\"{statusMode}\"}}";
                            break;

                        case "MODE SCREEN":
                            _modeManager.SetMode(LightingMode.ScreenCapture);
                            response = "{\"status\":\"ok\",\"mode\":\"screen\"}";
                            break;

                        case "MODE SOUND":
                            _modeManager.SetMode(LightingMode.SoundToLight);
                            response = "{\"status\":\"ok\",\"mode\":\"sound\"}";
                            break;

                        case "MODE GAMER":
                            _modeManager.SetMode(LightingMode.GamerMode);
                            response = "{\"status\":\"ok\",\"mode\":\"gamer\"}";
                            break;

                        case "MODE STATUS":
                            response = $"{{\"mode\":\"{ModeToString(_modeManager.ActiveMode)}\"}}";
                            break;

                        case "EXIT":
                            response = "{\"status\":\"exiting\"}";
                            var responseBytes = Encoding.ASCII.GetBytes(response);
                            stream.Write(responseBytes, 0, responseBytes.Length);
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                System.Windows.Application.Current.Shutdown(0));
                            return;

                        default:
                            response = "{\"status\":\"unknown_command\"}";
                            break;
                    }

                    var bytes = Encoding.ASCII.GetBytes(response);
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception ex)
            {
                _log.Warn(ex, "TcpControlServer error handling client.");
            }
        }

        private static string ModeToString(LightingMode mode) => mode switch
        {
            LightingMode.ScreenCapture => "screen",
            LightingMode.SoundToLight  => "sound",
            LightingMode.GamerMode     => "gamer",
            _                          => "screen"
        };

        public void Dispose()
        {
            Stop();
        }
    }
}
