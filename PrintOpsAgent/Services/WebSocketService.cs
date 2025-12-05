using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PrintOpsAgent.Services;

public class WebSocketService : IDisposable
{
    private readonly ILogger<WebSocketService> _logger;
    private readonly ConfigurationService _config;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private bool _isConnected;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public event EventHandler<JObject>? CommandReceived;
    public bool IsConnected => _isConnected;

    public WebSocketService(ILogger<WebSocketService> logger, ConfigurationService config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task ConnectAsync(CancellationToken stoppingToken)
    {
        if (!_config.IsConfigured)
        {
            _logger.LogWarning("Agent not configured, skipping WebSocket connection");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectOnceAsync();
                await ReceiveLoopAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket connection error, reconnecting in 10 seconds...");
                _isConnected = false;
                await Task.Delay(10000, stoppingToken);
            }
        }
    }

    private async Task ConnectOnceAsync()
    {
        _webSocket?.Dispose();
        _webSocket = new ClientWebSocket();
        
        // Add API key header
        _webSocket.Options.SetRequestHeader("X-API-Key", _config.Config.ApiKey);

        var wsUrl = _config.Config.DashboardUrl
            .Replace("https://", "wss://")
            .Replace("http://", "ws://")
            + "/ws/agent";

        _logger.LogInformation("Connecting to WebSocket: {Url}", wsUrl);
        
        await _webSocket.ConnectAsync(new Uri(wsUrl), _cts!.Token);
        _isConnected = true;
        
        _logger.LogInformation("WebSocket connected successfully");
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[8192];
        var messageBuilder = new StringBuilder();

        while (_webSocket?.State == WebSocketState.Open && !_cts!.IsCancellationRequested)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(buffer, _cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket closed by server");
                    _isConnected = false;
                    break;
                }

                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var message = messageBuilder.ToString();
                    messageBuilder.Clear();
                    
                    ProcessMessage(message);
                }
            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                _logger.LogWarning("WebSocket connection closed prematurely");
                _isConnected = false;
                break;
            }
        }
    }

    private void ProcessMessage(string message)
    {
        try
        {
            var json = JObject.Parse(message);
            var messageType = json["type"]?.ToString();

            _logger.LogDebug("Received message: {Type}", messageType);

            if (messageType == "command")
            {
                var command = json["command"] as JObject;
                if (command != null)
                {
                    CommandReceived?.Invoke(this, command);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process WebSocket message: {Message}", message);
        }
    }

    public async Task SendAsync(object data)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            _logger.LogWarning("Cannot send message, WebSocket not connected");
            return;
        }

        await _sendLock.WaitAsync();
        try
        {
            var json = JsonConvert.SerializeObject(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, _cts!.Token);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task SendHeartbeatAsync(object heartbeatData)
    {
        await SendAsync(new { type = "heartbeat", payload = heartbeatData });
    }

    public async Task SendCommandResultAsync(int commandId, object result)
    {
        await SendAsync(new { type = "command_result", commandId, result });
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _webSocket?.Dispose();
        _sendLock.Dispose();
    }
}
