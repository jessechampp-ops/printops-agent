# Build PrintOps Windows Agent with GitHub

## What You'll Get
A ready-to-run `PrintOpsAgent.exe` that installs on any Windows PC and connects to your PrintOps dashboard.

---

## Step-by-Step Instructions

### 1. Download the Agent Code

In Replit, click the three dots menu on the `windows-agent` folder and select **Download as ZIP**.

### 2. Create a GitHub Repository

1. Go to **[github.com/new](https://github.com/new)**
2. Repository name: `printops-agent`
3. Select **Public** (free unlimited builds)
4. Click **Create repository**

### 3. Upload the Code

1. On your new repo page, click **"uploading an existing file"** link
2. Extract the ZIP you downloaded
3. Drag ALL files and folders into the upload area:
   - `.github/` folder (contains the build workflow)
   - `PrintOpsAgent/` folder (the agent code)
   - `PrintOpsAgent.Installer/` folder
   - `README.md`
   - etc.
4. Click **Commit changes**

### 4. Wait for Build (2-3 minutes)

1. Click the **Actions** tab in your repo
2. You'll see "Build PrintOps Windows Agent" with a yellow dot (running)
3. Wait for it to turn into a green checkmark

### 5. Download Your Windows Agent

1. Click on the completed workflow run (green checkmark)
2. Scroll down to the **Artifacts** section
3. Click **PrintOpsAgent-win-x64** to download
4. Extract the ZIP file
5. Your `PrintOpsAgent.exe` is ready!

---

## Install on Windows

### Quick Test
Just double-click `PrintOpsAgent.exe` to run it manually.

### Install as Windows Service (Production)
```powershell
# Run PowerShell as Administrator

# Create config folder
mkdir "C:\ProgramData\PrintOps"

# Create config file (edit with your dashboard URL)
@"
{
  "DashboardUrl": "https://YOUR-REPLIT-URL.replit.app",
  "ApiKey": "your-api-key"
}
"@ | Out-File "C:\ProgramData\PrintOps\config.json"

# Copy agent
mkdir "C:\Program Files\PrintOps Agent"
copy PrintOpsAgent.exe "C:\Program Files\PrintOps Agent\"

# Install service
sc.exe create PrintOpsAgent binPath= "C:\Program Files\PrintOps Agent\PrintOpsAgent.exe" start= auto

# Start service
sc.exe start PrintOpsAgent
```

---

## Creating Releases

When ready for production, tag a version:

```bash
git tag v1.0.0
git push origin v1.0.0
```

This creates a GitHub Release page with a downloadable ZIP that anyone can use.

---

## Troubleshooting

**Build failed?**
- Make sure `.github/workflows/build.yml` was uploaded
- Check the Actions tab for error messages

**Agent won't connect?**
- Verify `config.json` has correct dashboard URL
- Check Windows Firewall allows outbound HTTPS

**Need help?**
Open an issue on your GitHub repo or contact PrintOps support.
