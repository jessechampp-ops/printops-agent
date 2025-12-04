# PrintOps Windows Agent

Windows Service agent for PrintOps Dashboard that provides real printer management capabilities on Windows machines.

---

## Quick Start: Build with GitHub Actions (10 Minutes)

### Step 1: Create a GitHub Repository

1. Go to [github.com](https://github.com) and sign in (or create a free account)
2. Click the **+** button → **New repository**
3. Name it `printops-agent`
4. Keep it **Public** (free builds) or **Private**
5. Click **Create repository**

### Step 2: Upload the Agent Code

**Option A: Using GitHub Web Upload**
1. Download the `windows-agent` folder from your Replit project
2. In your new GitHub repo, click **"Add file"** → **"Upload files"**
3. Drag and drop all files from the `windows-agent` folder
4. Click **Commit changes**

**Option B: Using Git Command Line**
```bash
git clone https://github.com/YOUR_USERNAME/printops-agent.git
cd printops-agent
# Copy windows-agent contents here
git add .
git commit -m "Initial commit"
git push origin main
```

### Step 3: Download Your Windows Executable

1. Go to your repository on GitHub
2. Click the **Actions** tab
3. You'll see "Build PrintOps Windows Agent" running automatically
4. Wait 2-3 minutes for it to complete (green checkmark)
5. Click on the completed workflow run
6. Scroll down to **Artifacts**
7. Download **PrintOpsAgent-win-x64**
8. Extract the ZIP - your `PrintOpsAgent.exe` is ready!

### Step 4: Create Official Releases

When ready for production, create a versioned release:

```bash
git tag v1.0.0
git push origin v1.0.0
```

This automatically creates a GitHub Release with a downloadable ZIP.

---

## Features

- **Printer Discovery**: Automatically detects all printers on the Windows machine
- **Status Monitoring**: Real-time status updates via WebSocket or HTTP heartbeat
- **Print Spooler Control**: Restart, stop, and manage the Windows Print Spooler service
- **Queue Management**: Clear print queues for individual printers
- **Driver Installation**: Install printer drivers via pnputil
- **Test Printing**: Send test pages to verify printer functionality
- **Remote Commands**: Execute commands from PrintOps Dashboard in real-time

## Requirements

- Windows 10/11 or Windows Server 2016+
- .NET 8.0 Runtime (included in self-contained build)
- Administrator privileges for installation

## Installation

### Manual Installation

1. Download `PrintOpsAgent.exe` from GitHub Releases
2. Copy to `C:\Program Files\PrintOps Agent\`
3. Open PowerShell as Administrator
4. Create the service:
   ```powershell
   sc create PrintOpsAgent binPath= "C:\Program Files\PrintOps Agent\PrintOpsAgent.exe" start= auto
   ```
5. Create configuration file at `C:\ProgramData\PrintOps\config.json`:
   ```json
   {
     "AgentId": "your-agent-id",
     "ApiKey": "your-api-key",
     "DashboardUrl": "https://your-printops-instance.replit.app",
     "HeartbeatIntervalSeconds": 30,
     "UseWebSocket": true
   }
   ```
6. Start the service:
   ```powershell
   sc start PrintOpsAgent
   ```

### MSI Installer (Silent Install)

```powershell
msiexec /i PrintOpsAgent.msi /qn APIKEY="your-api-key" DASHBOARDURL="https://your-dashboard.replit.app"
```

## Configuration

Configuration file location: `C:\ProgramData\PrintOps\config.json`

| Setting | Description | Default |
|---------|-------------|---------|
| `AgentId` | Unique identifier for this agent | Auto-generated |
| `ApiKey` | API key from PrintOps Dashboard | Required |
| `DashboardUrl` | URL of your PrintOps instance | Required |
| `HeartbeatIntervalSeconds` | How often to send status updates | 30 |
| `UseWebSocket` | Use WebSocket for real-time commands | true |

## Commands

The agent responds to the following commands from the dashboard:

| Command | Description |
|---------|-------------|
| `restart_spooler` | Restart the Windows Print Spooler service |
| `clear_queue` | Clear all pending print jobs for a printer |
| `fix_printer` | Run automated diagnostic and repair steps |
| `test_print` | Send a Windows test page to a printer |
| `get_status` | Get current status of one or all printers |
| `install_driver` | Install a printer driver from local path |
| `update_driver` | Update printer driver to latest version |

## Service Management

```powershell
# Check service status
sc query PrintOpsAgent

# Stop service
sc stop PrintOpsAgent

# Start service
sc start PrintOpsAgent

# Restart service
sc stop PrintOpsAgent && sc start PrintOpsAgent

# Remove service
sc delete PrintOpsAgent
```

## Logs

Service logs are available via Windows Event Viewer:
- Application and Services Logs > PrintOps Agent

Or via PowerShell:
```powershell
Get-EventLog -LogName Application -Source "PrintOps Agent" -Newest 50
```

## Building Locally (Alternative to GitHub Actions)

Prerequisites:
- .NET 8.0 SDK
- PowerShell 5.0+
- WiX Toolset 4.0+ (for MSI installer)

Build steps:
```powershell
# Build self-contained executable
dotnet publish PrintOpsAgent/PrintOpsAgent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Output will be in: PrintOpsAgent/bin/Release/net8.0/win-x64/publish/
```

## Troubleshooting

### Agent not connecting
1. Verify `DashboardUrl` is correct and accessible
2. Check `ApiKey` is valid
3. Ensure firewall allows outbound HTTPS/WSS on port 443

### Printers not detected
1. Verify Print Spooler service is running
2. Check agent is running with Administrator privileges
3. Run `Get-Printer` in PowerShell to verify Windows sees printers

### Commands not executing
1. Check WebSocket connection in agent logs
2. Verify agent ID matches dashboard
3. Ensure agent has sufficient permissions

## Security

- All communication uses HTTPS/WSS
- API keys are stored securely in Windows credential store
- Commands are authenticated and authorized by dashboard
- Agent runs as LocalSystem with minimal required permissions

## License

Proprietary - PrintOps
