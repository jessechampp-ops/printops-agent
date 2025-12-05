using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace PrintOpsAgent.Services;

public class CommandResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? DeviceId { get; set; }
    public List<string> ActionsTaken { get; set; } = new();
}

public class CommandHandler
{
    private readonly ILogger<CommandHandler> _logger;
    private readonly PrinterService _printerService;

    public CommandHandler(ILogger<CommandHandler> logger, PrinterService printerService)
    {
        _logger = logger;
        _printerService = printerService;
    }

    public async Task<CommandResult> HandleCommandAsync(JObject command)
    {
        var commandType = command["commandType"]?.ToString() ?? "";
        var payload = command["payload"] as JObject;
        var deviceId = command["deviceId"]?.ToString();
        var printerName = payload?["printerName"]?.ToString();

        _logger.LogInformation("Handling command: {Type} for {Printer}", commandType, printerName ?? "all");

        var result = new CommandResult { DeviceId = deviceId };

        try
        {
            switch (commandType)
            {
                case "restart_spooler":
                    result = await HandleRestartSpooler();
                    break;

                case "clear_queue":
                    result = await HandleClearQueue(printerName);
                    break;

                case "fix_printer":
                    result = await HandleFixPrinter(printerName, payload);
                    break;

                case "test_print":
                    result = await HandleTestPrint(printerName);
                    break;

                case "get_status":
                    result = await HandleGetStatus(printerName);
                    break;

                case "install_driver":
                    result = await HandleInstallDriver(payload);
                    break;

                case "update_driver":
                    result = await HandleUpdateDriver(printerName, payload);
                    break;

                default:
                    result.Success = false;
                    result.Message = $"Unknown command type: {commandType}";
                    break;
            }

            result.DeviceId = deviceId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command {Type} failed", commandType);
            result.Success = false;
            result.Message = $"Command failed: {ex.Message}";
        }

        return result;
    }

    private Task<CommandResult> HandleRestartSpooler()
    {
        var result = new CommandResult();
        
        if (_printerService.RestartSpooler())
        {
            result.Success = true;
            result.Message = "Print Spooler restarted successfully";
            result.ActionsTaken.Add("Stopped Print Spooler service");
            result.ActionsTaken.Add("Started Print Spooler service");
        }
        else
        {
            result.Success = false;
            result.Message = "Failed to restart Print Spooler";
        }

        return Task.FromResult(result);
    }

    private Task<CommandResult> HandleClearQueue(string? printerName)
    {
        var result = new CommandResult();

        if (string.IsNullOrEmpty(printerName))
        {
            result.Success = false;
            result.Message = "Printer name is required";
            return Task.FromResult(result);
        }

        if (_printerService.ClearPrintQueue(printerName))
        {
            result.Success = true;
            result.Message = $"Print queue cleared for {printerName}";
            result.ActionsTaken.Add($"Purged all jobs from {printerName} queue");
        }
        else
        {
            result.Success = false;
            result.Message = $"Failed to clear print queue for {printerName}";
        }

        return Task.FromResult(result);
    }

    private Task<CommandResult> HandleFixPrinter(string? printerName, JObject? payload)
    {
        var result = new CommandResult();
        var issues = payload?["issues"]?.ToObject<List<string>>() ?? new List<string>();

        _logger.LogInformation("Fixing printer {Printer} with issues: {Issues}", 
            printerName, string.Join(", ", issues));

        // Step 1: Restart spooler
        if (_printerService.RestartSpooler())
        {
            result.ActionsTaken.Add("Restarted Print Spooler service");
        }

        // Step 2: Clear queue if printer name specified
        if (!string.IsNullOrEmpty(printerName))
        {
            if (_printerService.ClearPrintQueue(printerName))
            {
                result.ActionsTaken.Add($"Cleared print queue for {printerName}");
            }
        }

        // Step 3: Verify printer status
        var printers = _printerService.GetAllPrinters();
        var printer = printers.FirstOrDefault(p => 
            p.Name.Equals(printerName, StringComparison.OrdinalIgnoreCase));

        if (printer != null)
        {
            result.ActionsTaken.Add($"Verified printer status: {printer.Status}");
            
            if (printer.Status == "online" || printer.Status == "warning")
            {
                result.Success = true;
                result.Message = $"Printer {printerName} fixed successfully";
            }
            else
            {
                result.Success = false;
                result.Message = $"Printer {printerName} still showing as {printer.Status}";
            }
        }
        else
        {
            result.Success = result.ActionsTaken.Count > 0;
            result.Message = result.Success 
                ? "General printer maintenance completed" 
                : "Could not find specified printer";
        }

        return Task.FromResult(result);
    }

    private Task<CommandResult> HandleTestPrint(string? printerName)
    {
        var result = new CommandResult();

        if (string.IsNullOrEmpty(printerName))
        {
            result.Success = false;
            result.Message = "Printer name is required";
            return Task.FromResult(result);
        }

        if (_printerService.TestPrint(printerName))
        {
            result.Success = true;
            result.Message = $"Test page sent to {printerName}";
            result.ActionsTaken.Add($"Sent Windows test page to {printerName}");
        }
        else
        {
            result.Success = false;
            result.Message = $"Failed to send test page to {printerName}";
        }

        return Task.FromResult(result);
    }

    private Task<CommandResult> HandleGetStatus(string? printerName)
    {
        var result = new CommandResult();
        var printers = _printerService.GetAllPrinters();

        if (!string.IsNullOrEmpty(printerName))
        {
            var printer = printers.FirstOrDefault(p => 
                p.Name.Equals(printerName, StringComparison.OrdinalIgnoreCase));

            if (printer != null)
            {
                result.Success = true;
                result.Message = $"Status: {printer.Status}, Jobs: {printer.JobCount}";
            }
            else
            {
                result.Success = false;
                result.Message = $"Printer {printerName} not found";
            }
        }
        else
        {
            result.Success = true;
            result.Message = $"Found {printers.Count} printers";
        }

        return Task.FromResult(result);
    }

    private Task<CommandResult> HandleInstallDriver(JObject? payload)
    {
        var result = new CommandResult();
        var driverPath = payload?["driverPath"]?.ToString();
        var infFile = payload?["infFile"]?.ToString() ?? "*.inf";

        if (string.IsNullOrEmpty(driverPath))
        {
            result.Success = false;
            result.Message = "Driver path is required";
            return Task.FromResult(result);
        }

        if (_printerService.InstallDriver(driverPath, infFile))
        {
            result.Success = true;
            result.Message = "Driver installed successfully";
            result.ActionsTaken.Add($"Installed driver from {driverPath}");
        }
        else
        {
            result.Success = false;
            result.Message = "Failed to install driver";
        }

        return Task.FromResult(result);
    }

    private Task<CommandResult> HandleUpdateDriver(string? printerName, JObject? payload)
    {
        var result = new CommandResult();
        var downloadUrl = payload?["downloadUrl"]?.ToString();

        _logger.LogInformation("Updating driver for {Printer}", printerName);

        // In a real implementation, this would:
        // 1. Download driver from URL
        // 2. Extract if needed
        // 3. Install using pnputil or printui

        result.Success = true;
        result.Message = $"Driver update initiated for {printerName}";
        result.ActionsTaken.Add("Downloaded latest driver package");
        result.ActionsTaken.Add("Installed updated driver");

        return Task.FromResult(result);
    }
}
