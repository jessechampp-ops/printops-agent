using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PrintOpsAgent.Services;

public class AgentConfiguration
{
    public string AgentId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string DashboardUrl { get; set; } = "https://your-printops-dashboard.replit.app";
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public bool UseWebSocket { get; set; } = true;
}

public class ConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;
    private readonly string _configPath;
    private AgentConfiguration _config;

    public ConfigurationService(ILogger<ConfigurationService> logger)
    {
        _logger = logger;
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PrintOps",
            "config.json"
        );
        _config = new AgentConfiguration();
        LoadConfiguration();
    }

    public AgentConfiguration Config => _config;

    public bool IsConfigured => !string.IsNullOrEmpty(_config.ApiKey) && !string.IsNullOrEmpty(_config.DashboardUrl);

    private void LoadConfiguration()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<AgentConfiguration>(json) ?? new AgentConfiguration();
                _logger.LogInformation("Configuration loaded from {Path}", _configPath);
            }
            else
            {
                _logger.LogWarning("Configuration file not found at {Path}", _configPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration");
        }
    }

    public void SaveConfiguration(AgentConfiguration config)
    {
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            _config = config;
            _logger.LogInformation("Configuration saved to {Path}", _configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            throw;
        }
    }

    public void Configure(string apiKey, string dashboardUrl)
    {
        _config.ApiKey = apiKey;
        _config.DashboardUrl = dashboardUrl.TrimEnd('/');
        SaveConfiguration(_config);
    }
}
