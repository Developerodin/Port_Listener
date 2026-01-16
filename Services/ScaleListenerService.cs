using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using PortListener.Services;

namespace PortListener.Services;

public class ScaleListenerService : BackgroundService
{
    private readonly IWeightStorageService _weightStorage;
    private readonly ILogger<ScaleListenerService> _logger;

    // Only accept data from this sender IP
    private readonly IPAddress _allowedSenderIP = IPAddress.Parse("127.0.0.1");
    private readonly int _tcpPort = 3666;

    public ScaleListenerService(
        IWeightStorageService weightStorage,
        ILogger<ScaleListenerService> logger)
    {
        _weightStorage = weightStorage;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, _tcpPort);
            listener.Start();

            _logger.LogInformation("Listening on TCP port {Port}...", _tcpPort);
            _logger.LogInformation("Only accepting connections from: {IP}", _allowedSenderIP);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Accept TCP connection - cancellation is handled by checking the token
                    var acceptTask = listener.AcceptTcpClientAsync();
                    var cancellationTask = Task.Delay(Timeout.Infinite, stoppingToken);
                    
                    var completedTask = await Task.WhenAny(acceptTask, cancellationTask);
                    if (completedTask == cancellationTask)
                    {
                        // Cancellation requested, break out of loop
                        break;
                    }
                    
                    var client = await acceptTask;
                    var clientEndPoint = client.Client.RemoteEndPoint as IPEndPoint;

                    // Filter: Only accept connections from the allowed sender IP
                    if (clientEndPoint == null || !clientEndPoint.Address.Equals(_allowedSenderIP))
                    {
                        _logger.LogDebug(
                            "Rejected connection from {Address}:{Port} (only accepting from {AllowedIP})",
                            clientEndPoint?.Address, clientEndPoint?.Port, _allowedSenderIP);
                        client.Close();
                        continue;
                    }

                    _logger.LogInformation("Accepted TCP connection from {Address}:{Port}", 
                        clientEndPoint.Address, clientEndPoint.Port);

                    // Process connection in background (don't await to allow multiple connections)
                    _ = Task.Run(async () => await ProcessTcpClientAsync(client, stoppingToken), stoppingToken);
                }
                catch (SocketException ex)
                {
                    _logger.LogError(ex, "Socket error while accepting TCP connection");
                    await Task.Delay(1000, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error accepting TCP connection");
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in ScaleListenerService");
        }
        finally
        {
            listener?.Stop();
        }
    }

    private async Task ProcessTcpClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var buffer = new byte[1024];
                
                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    try
                    {
                        // Read data from TCP stream
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        
                        if (bytesRead == 0)
                        {
                            // Connection closed by client
                            break;
                        }

                        string text = Encoding.ASCII.GetString(buffer, 0, bytesRead).Split('\0')[0].Trim();

                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        // Extract weight value from message (format: RTW:0.650 kg)
                        string? weight = ExtractWeight(text);

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

                        _logger.LogInformation("Message: {Message}", text);
                        if (weight != null)
                        {
                            _logger.LogInformation("Weight: {Weight} kg", weight);
                        }
                    }
                    catch (IOException ex)
                    {
                        _logger.LogWarning(ex, "Connection closed or error reading from stream");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing TCP data");
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing TCP client");
        }
    }

    private static string? ExtractWeight(string message)
    {
        // Match pattern like "RTW:0.650 kg" or similar formats
        var match = Regex.Match(message, @"RTW:([\d.]+)\s*kg", RegexOptions.IgnoreCase);
        if (match.Success && match.Groups.Count > 1)
        {
            return match.Groups[1].Value;
        }
        return null;
    }
}
