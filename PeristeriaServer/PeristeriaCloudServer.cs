using System.Diagnostics;


namespace PeristeriaServer;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

internal class PeristeriaCloudServer {
    const int MaxPayloadBytes = 1 * 1024 * 1024;
    static readonly TimeSpan RateLimitCooldown = TimeSpan.FromSeconds(1);
 
    static readonly ConcurrentDictionary<string, DateTime> _lastRequestTime = new();
    const int PORT = 9000;
    const int HEADER_LENGTH = 1; //Makes packet alignment kinda shit, but it's a small enough packet that we'd have to pad anyway if we cared about that

    enum HeaderEnum {
        SaveData = 0,
        LeaderboardData = 1,
        MetricsData = 2,
        LoadRequest = 3
    }
    
    static readonly SemaphoreSlim _metricsLock = new(1, 1);

    
    
    public async static Task Main(string[] args) {
        PeristeriaCloudServer server = new PeristeriaCloudServer();
        _ = server.Serve(args);
        await Task.Delay(-1); // Block indefinitely
    }
    
    public async Task Serve(string[] args) {
        TcpListener listener = new TcpListener(IPAddress.Any, 9000);
        Console.WriteLine($"{IPAddress.Any}");
        listener.Start();
        Console.WriteLine($"Server started: Hosting on port {PORT}");

        while (true) {
            TcpClient client = await listener.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
    }
    async Task HandleClientAsync(TcpClient client) {
        Console.WriteLine($"{client.Client.RemoteEndPoint}");
        string remoteIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
        
        
        // Per-IP rate limiting
        DateTime now = DateTime.UtcNow;
        if (_lastRequestTime.TryGetValue(remoteIp, out DateTime lastTime) &&
            now - lastTime < RateLimitCooldown) {
            Console.WriteLine($"Rate limited: {remoteIp}");
            client.Close();
            return;
        }
        _lastRequestTime[remoteIp] = now;

        try {
            NetworkStream stream = client.GetStream();
            byte[] headerField = new byte[HEADER_LENGTH];
            await stream.ReadExactlyAsync(headerField, 0, 1);
            byte headerValue = headerField[0];
            HeaderEnum header = (HeaderEnum)headerValue;
            
            byte[] payloadLengthBuffer = new byte[4];
            await stream.ReadExactlyAsync(payloadLengthBuffer, 0, 4);
            int payloadLength = BitConverter.ToInt32(payloadLengthBuffer, 0);

            // Payload size guard
            if (payloadLength < 0 || payloadLength > MaxPayloadBytes) {
                Console.WriteLine($"Rejected oversized or invalid payload ({payloadLength} bytes) from {remoteIp}");
                client.Close();
                return;
            }

            byte[] nameLengthBuffer = new byte[4];
            await stream.ReadExactlyAsync(nameLengthBuffer, 0, 4);
            int nameLength = BitConverter.ToInt32(nameLengthBuffer, 0);

            byte[] nameBuffer = new byte[nameLength];
            await stream.ReadExactlyAsync(nameBuffer, 0, nameLength);
            string name = Encoding.UTF8.GetString(nameBuffer);

            if (header == HeaderEnum.LoadRequest) {
                // Load request
                await HandleLoadAsync(stream, name);
            } else {
                // Save request
                byte[] messageBuffer = new byte[payloadLength];
                await stream.ReadExactlyAsync(messageBuffer, 0, payloadLength);
                string json = Encoding.UTF8.GetString(messageBuffer);
                
                if (header == HeaderEnum.SaveData) {
                    Directory.CreateDirectory("Saves");
                    Directory.CreateDirectory($"Saves/{name}");
                    Console.WriteLine($"Saving to {name}");
                    await File.WriteAllTextAsync($"Saves/{name}/Save.json", json);
                    Console.WriteLine($"Saved to {name}");
                } 
                else if (header == HeaderEnum.LeaderboardData) {
                    Directory.CreateDirectory("Leaderboard");
                    Directory.CreateDirectory($"Leaderboard/{name}");
                    await File.WriteAllTextAsync($"Leaderboard/{name}/Time.json", json);
                }
                else if (header == HeaderEnum.MetricsData) {
                    Directory.CreateDirectory("Metrics");
                    Directory.CreateDirectory($"Metrics/{name}");
                    await _metricsLock.WaitAsync();
                    try {
                        await File.AppendAllTextAsync($"Metrics/{name}/Save.json", json + "\n");
                    } finally {
                        _metricsLock.Release();
                    }
                }
                Console.WriteLine($"Received: {json} from {name}");
                byte[] response = Encoding.UTF8.GetBytes("OK");
                byte[] lengthPrefix = BitConverter.GetBytes(response.Length);
                await stream.WriteAsync(lengthPrefix);
                await stream.WriteAsync(response);
            }
        }
        finally {
            client.Close();
        }
        
        

    }
    
    
    async Task HandleLoadAsync(NetworkStream stream, string name) {
        string path = $"Saves/{name}/Save.json";
    
        if (!File.Exists(path)) {
            Console.WriteLine($"Save not found for {name}");
            byte[] empty = BitConverter.GetBytes(0);
            await stream.WriteAsync(empty);
            return;
        }

        string json = await File.ReadAllTextAsync(path);
        byte[] response = Encoding.UTF8.GetBytes(json);
        byte[] lengthPrefix = BitConverter.GetBytes(response.Length);
    
        Console.WriteLine($"Sending save ({response.Length} bytes) to {name}");
        await stream.WriteAsync(lengthPrefix);
        await stream.WriteAsync(response);
    }
}

