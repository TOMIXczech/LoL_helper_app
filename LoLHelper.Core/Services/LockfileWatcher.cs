using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LoLHelper.Core.Services
{
    public class LcuCredentials
    {
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public int Port { get; set; }
        public string Password { get; set; } = string.Empty;
        public string Protocol { get; set; } = string.Empty;
        public string InstallDirectory { get; set; } = string.Empty;

        public string BaseUrl => $"{Protocol}://127.0.0.1:{Port}";
        
        public string GetBasicAuthHeader()
        {
            var authString = $"riot:{Password}";
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));
            return $"Basic {base64}";
        }
    }

    public class LockfileWatcher
    {
        private readonly string _installDirectory;
        private readonly string _lockfilePath;
        private CancellationTokenSource? _cts;

        public event Action<LcuCredentials>? OnClientConnected;
        public event Action? OnClientDisconnected;

        public LockfileWatcher(string installDirectory = @"C:\Riot Games\League of Legends")
        {
            _installDirectory = installDirectory;
            _lockfilePath = Path.Combine(_installDirectory, "lockfile");
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => WatchLoop(_cts.Token), _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        private async Task WatchLoop(CancellationToken token)
        {
            bool wasConnected = false;

            while (!token.IsCancellationRequested)
            {
                bool isConnected = File.Exists(_lockfilePath);

                if (isConnected && !wasConnected)
                {
                    // Wait a brief moment to ensure the file is fully written by the client
                    await Task.Delay(1000, token);
                    
                    try
                    {
                        var credentials = ReadLockfile();
                        if (credentials != null)
                        {
                            wasConnected = true;
                            OnClientConnected?.Invoke(credentials);
                        }
                    }
                    catch (Exception)
                    {
                        // File might be locked, will try again next loop
                    }
                }
                else if (!isConnected && wasConnected)
                {
                    wasConnected = false;
                    OnClientDisconnected?.Invoke();
                }

                await Task.Delay(2000, token);
            }
        }

        private LcuCredentials? ReadLockfile()
        {
            // Read with FileShare.ReadWrite because the League Client holds a lock on it
            using var fs = new FileStream(_lockfilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            var content = sr.ReadToEnd();

            // Format: process:pid:port:password:protocol
            var parts = content.Split(':');
            if (parts.Length == 5)
            {
                return new LcuCredentials
                {
                    ProcessName = parts[0],
                    ProcessId = int.Parse(parts[1]),
                    Port = int.Parse(parts[2]),
                    Password = parts[3],
                    Protocol = parts[4],
                    InstallDirectory = _installDirectory
                };
            }

            return null;
        }
    }
}
