using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using PrintOpsAgent.Services;

namespace PrintOpsAgent;

public class PrintOpsAgentWorker : BackgroundService
{
    private readonly ILogger<PrintOpsAgentWorker> _logger;
    private readonly ConfigurationService _config;
    private readonly PrinterService _printerService;
    private readonly WebSocketService _webSocketService;
    private readonly CommandHandler _commandHandler;

    public PrintOpsAgentWorker(
        ILogger<PrintOpsAgentWorker> logger,
        ConfigurationService config,
        PrinterService printerService,
        WebSocketService webSocketService,
        CommandHandler commandHandler)
    {
        _logger = logger;
        _config = config;
        _printerService = printerService;
        _webSocketService = webSocketService;
        _commandHandler = commandHandler;

        _webSocketService.CommandReceived += OnCommandReceived;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PrintOps Agent starting...");

        if (!_config.IsConfigured)
        {
            _logger.LogWarning("Agent not configured. Please run configuration wizard or provide config.json");
            _logger.LogInformation("Configuration file location: {Path}", 
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PrintOps", "config.json"));
            
            // Wait for configuration
            while (!_config.IsConfigured && !stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(5000, stoppingToken);
            }
        }

        _logger.LogInformation("Agent configured for {Url}", _config.Config.DashboardUrl);

        // Start WebSocket connection in background
        var wsTask = _webSocketService.ConnectAsync(stoppingToken);

        // Start heartbeat loop
        var heartbeatTask = HeartbeatLoopAsync(stoppingToken);

        await Task.WhenAll(wsTask, heartbeatTask);
    }

    private async Task HeartbeatLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendHeartbeatAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Heartbeat failed");
            }

            await Task.Delay(_config.Config.HeartbeatIntervalSeconds * 1000, stoppingToken);
        }
    }

    private async Task SendHeartbeatAsync()
    {
        var printers = _printerService.GetAllPrinters();
        
        var heartbeat = new
        {
            agentId = _config.Config.AgentId,
            hostname = Environment.MachineName,
            osVersion = Environment.OSVersion.ToString(),
            agentVersion = "1.0.0",
            ipAddress = GetLocalIPAddress(),
            printers = printers.Select(p => new
            {
                name = p.Name,
                model = p.Model,
                manufacturer = p.Manufacturer,
                port = p.Port,
                status = p.Status,
                driverVersion = p.DriverVersion,
                driverStatus = p.DriverStatus,
                inkLevels = p.InkLevels != null ? new { c = p.InkLevels.C, m = p.InkLevels.M, y = p.InkLevels.Y, k = p.InkLevels.K } : null,
                jobCount = p.JobCount
            }).ToList()
        };

        if (_webSocketService.IsConnected)
        {
            await _webSocketService.SendHeartbeatAsync(heartbeat);
            _logger.LogDebug("Heartbeat sent via WebSocket ({Printers} printers)", printers.Count);
        }
        else
        {
            // Fallback to HTTP
            await SendHeartbeatHttpAsync(heartbeat);
        }
    }

    private async Task SendHeartbeatHttpAsync(object heartbeat)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-API-Key", _config.Config.ApiKey);

            var response = await client.PostAsJsonAsync(
                $"{_config.Config.DashboardUrl}/api/agents/heartbeat",
                heartbeat
            );

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Heartbeat sent via HTTP");
                
                // Check for pending commands
                var result = await response.Content.ReadFromJsonAsync<HeartbeatResponse>();
                if (result?.Commands != null)
                {
                    foreach (var command in result.Commands)
                    {
                        OnCommandReceived(this, command);
                    }
                }
            }
            else
            {
                _logger.LogWarning("Heartbeat HTTP failed: {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP heartbeat failed");
        }
    }

    private async void OnCommandReceived(object? sender, JObject command)
    {
        _logger.LogInformation("Command received: {Type}", command["commandType"]);

        try
        {
            var result = await _commandHandler.HandleCommandAsync(command);
            var commandId = command["id"]?.ToObject<int>() ?? 0;

            if (_webSocketService.IsConnected)
            {
                await _webSocketService.SendCommandResultAsync(commandId, result);
            }
            else
            {
                // Fallback to HTTP
                await SendCommandResultHttpAsync(commandId, result);
            }

            _logger.LogInformation("Command completed: {Success} - {Message}", result.Success, result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command handling failed");
        }
    }

    private async Task SendCommandResultHttpAsync(int commandId, object result)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-API-Key", _config.Config.ApiKey);

            await client.PostAsJsonAsync(
                $"{_config.Config.DashboardUrl}/api/agents/command/{commandId}/result",
                result
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command result");
        }
    }

    private string GetLocalIPAddress()
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as System.Net.IPEndPoint;
            return endPoint?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    public override void Dispose()
    {
        _webSocketService.CommandReceived -= OnCommandReceived;
        base.Dispose();
    }
}

public class HeartbeatResponse
{
    public bool Success { get; set; }
    public List<JObject>? Commands { get; set; }
}
