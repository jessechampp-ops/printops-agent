using System.Management;
using System.ServiceProcess;
using System.Printing;
using Microsoft.Extensions.Logging;

namespace PrintOpsAgent.Services;

public class PrinterInfo
{
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Port { get; set; } = "";
    public string Status { get; set; } = "unknown";
    public string DriverVersion { get; set; } = "";
    public string DriverStatus { get; set; } = "current";
    public InkLevels? InkLevels { get; set; }
    public int JobCount { get; set; }
}

public class InkLevels
{
    public int C { get; set; }
    public int M { get; set; }
    public int Y { get; set; }
    public int K { get; set; }
}

public class PrinterService
{
    private readonly ILogger<PrinterService> _logger;

    public PrinterService(ILogger<PrinterService> logger)
    {
        _logger = logger;
    }

    public List<PrinterInfo> GetAllPrinters()
    {
        var printers = new List<PrinterInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
            
            foreach (ManagementObject printer in searcher.Get())
            {
                var printerInfo = new PrinterInfo
                {
                    Name = printer["Name"]?.ToString() ?? "Unknown",
                    Model = printer["DriverName"]?.ToString() ?? "Unknown",
                    Manufacturer = ExtractManufacturer(printer["DriverName"]?.ToString()),
                    Port = printer["PortName"]?.ToString() ?? "Unknown",
                    Status = GetPrinterStatus(printer),
                    DriverVersion = GetDriverVersion(printer["DriverName"]?.ToString()),
                    DriverStatus = "current",
                    JobCount = GetJobCount(printer["Name"]?.ToString() ?? "")
                };

                printers.Add(printerInfo);
                _logger.LogDebug("Found printer: {Name} ({Model})", printerInfo.Name, printerInfo.Model);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate printers");
        }

        return printers;
    }

    private string ExtractManufacturer(string? driverName)
    {
        if (string.IsNullOrEmpty(driverName)) return "Unknown";
        
        var manufacturers = new[] { "HP", "Canon", "Brother", "Epson", "Zebra", "Lexmark", "Xerox", "Dell", "Samsung", "Ricoh", "Kyocera" };
        foreach (var mfr in manufacturers)
        {
            if (driverName.Contains(mfr, StringComparison.OrdinalIgnoreCase))
                return mfr;
        }
        return "Generic";
    }

    private string GetPrinterStatus(ManagementObject printer)
    {
        try
        {
            var printerStatus = Convert.ToInt32(printer["PrinterStatus"]);
            var workOffline = (bool)(printer["WorkOffline"] ?? false);

            if (workOffline) return "offline";
            
            return printerStatus switch
            {
                1 => "online",   // Other
                2 => "online",   // Unknown
                3 => "online",   // Idle
                4 => "warning",  // Printing
                5 => "warning",  // Warmup
                6 => "error",    // Stopped Printing
                7 => "offline",  // Offline
                _ => "warning"
            };
        }
        catch
        {
            return "unknown";
        }
    }

    private string GetDriverVersion(string? driverName)
    {
        if (string.IsNullOrEmpty(driverName)) return "Unknown";

        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_PrinterDriver WHERE Name LIKE '%{driverName.Replace("'", "''")}%'");
            
            foreach (ManagementObject driver in searcher.Get())
            {
                return driver["DriverVersion"]?.ToString() ?? "1.0.0";
            }
        }
        catch { }

        return "1.0.0";
    }

    private int GetJobCount(string printerName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_PrintJob WHERE Name LIKE '{printerName.Replace("'", "''")}%'");
            return searcher.Get().Count;
        }
        catch
        {
            return 0;
        }
    }

    public bool RestartSpooler()
    {
        try
        {
            _logger.LogInformation("Restarting Print Spooler service...");
            
            using var service = new ServiceController("Spooler");
            
            if (service.Status == ServiceControllerStatus.Running)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                _logger.LogInformation("Print Spooler stopped");
            }

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            
            _logger.LogInformation("Print Spooler started successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart Print Spooler");
            return false;
        }
    }

    public bool ClearPrintQueue(string printerName)
    {
        try
        {
            _logger.LogInformation("Clearing print queue for {Printer}", printerName);

            using var server = new LocalPrintServer();
            var queue = server.GetPrintQueues().FirstOrDefault(q => 
                q.Name.Equals(printerName, StringComparison.OrdinalIgnoreCase));

            if (queue == null)
            {
                _logger.LogWarning("Printer {Printer} not found", printerName);
                return false;
            }

            queue.Purge();
            _logger.LogInformation("Print queue cleared for {Printer}", printerName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear print queue for {Printer}", printerName);
            return false;
        }
    }

    public bool TestPrint(string printerName)
    {
        try
        {
            _logger.LogInformation("Sending test page to {Printer}", printerName);

            using var server = new LocalPrintServer();
            var queue = server.GetPrintQueues().FirstOrDefault(q => 
                q.Name.Equals(printerName, StringComparison.OrdinalIgnoreCase));

            if (queue == null)
            {
                _logger.LogWarning("Printer {Printer} not found", printerName);
                return false;
            }

            // Use Windows built-in test page command
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = $"printui.dll,PrintUIEntry /k /n \"{printerName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            process?.WaitForExit(10000);
            
            _logger.LogInformation("Test page sent to {Printer}", printerName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send test page to {Printer}", printerName);
            return false;
        }
    }

    public bool InstallDriver(string driverPath, string infFile)
    {
        try
        {
            _logger.LogInformation("Installing driver from {Path}", driverPath);

            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = $"/add-driver \"{Path.Combine(driverPath, infFile)}\" /install",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            process?.WaitForExit(120000);
            
            var output = process?.StandardOutput.ReadToEnd();
            var error = process?.StandardError.ReadToEnd();
            
            if (process?.ExitCode == 0)
            {
                _logger.LogInformation("Driver installed successfully: {Output}", output);
                return true;
            }
            else
            {
                _logger.LogError("Driver installation failed: {Error}", error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install driver");
            return false;
        }
    }

    public bool SetDefaultPrinter(string printerName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_Printer WHERE Name = '{printerName.Replace("'", "''")}'");
            
            foreach (ManagementObject printer in searcher.Get())
            {
                printer.InvokeMethod("SetDefaultPrinter", null);
                _logger.LogInformation("Set {Printer} as default", printerName);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set default printer");
            return false;
        }
    }
}
