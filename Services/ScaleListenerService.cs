using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PortListener.Services;

namespace PortListener.Services;

public class ScaleListenerService : BackgroundService
{
    private readonly IWeightStorageService _weightStorage;
    private readonly ILogger<ScaleListenerService> _logger;

    // Only accept data from this sender IP and port
    private readonly IPAddress _allowedSenderIP = IPAddress.Parse("192.168.0.199");
    private readonly int _allowedSenderPort = 5001;
    private readonly int _udpPort = 3666;
    
    private const string JsonFilePath = "data/scale_data.json";
    private readonly object _fileLock = new object();

    public ScaleListenerService(
        IWeightStorageService weightStorage,
        ILogger<ScaleListenerService> logger)
    {
        _weightStorage = weightStorage;
        _logger = logger;
        
        // Ensure data directory exists
        Directory.CreateDirectory("data");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        UdpClient? udp = null;
        try
        {
            // Use Socket directly to enable ReuseAddress
            udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, _udpPort));

            _logger.LogInformation("Listening on UDP port {Port}...", _udpPort);
            _logger.LogInformation("Only accepting data from: {IP}:{Port}", _allowedSenderIP, _allowedSenderPort);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = await udp.ReceiveAsync(stoppingToken);
                    var ep = result.RemoteEndPoint;
                    byte[] data = result.Buffer;

                    string text = Encoding.ASCII.GetString(data).Split('\0')[0].Trim();
                    
                    _logger.LogDebug("[DEBUG] Received packet from {Address}:{Port}", ep.Address, ep.Port);
                    _logger.LogDebug("[DEBUG] Raw Content: \"{Content}\"", text);

                    // Filter: Only accept data from the allowed sender IP and port
                    if (!ep.Address.Equals(_allowedSenderIP) || ep.Port != _allowedSenderPort)
                    {
                        _logger.LogWarning("Rejected packet from {Address}:{Port} (only accepting from {AllowedIP}:{AllowedPort})",
                            ep.Address, ep.Port, _allowedSenderIP, _allowedSenderPort);
                        continue;
                    }

                    // Extract weight value from message (format: "1.282KG")
                    string? weight = ExtractWeight(text);
                    
                    if (weight == null)
                    {
                        _logger.LogWarning("Could not extract weight from message: \"{Message}\"", text);
                    }

                    // Create data object
                    var dataEntry = new WeightData
                    {
                        Timestamp = DateTime.Now,
                        Message = text,
                        Weight = weight != null ? double.Parse(weight) : null,
                        WeightUnit = "kg"
                    };

                    // Update in-memory storage
                    _weightStorage.UpdateWeight(dataEntry);

                    // Save to JSON file (JSONL format)
                    string json = JsonSerializer.Serialize(dataEntry);
                    lock (_fileLock)
                    {
                        File.AppendAllText(JsonFilePath, json + Environment.NewLine);
                    }

                    _logger.LogInformation("✅ Processed Message: {Message}", text);
                    if (weight != null)
                    {
                        _logger.LogInformation("⚖️  Extracted Weight: {Weight} kg", weight);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing UDP packet");
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in ScaleListenerService");
        }
        finally
        {
            udp?.Close();
        }
    }

    private static string? ExtractWeight(string message)
    {
        // Support format like "RTW:0.650 kg" or "1.282KG"
        var match = Regex.Match(message, @"(?:RTW:)?([\d.]+)\s*(?:kg|KG)", RegexOptions.IgnoreCase);
        if (match.Success && match.Groups.Count > 1)
        {
            return match.Groups[1].Value;
        }
        return null;
    }
}
