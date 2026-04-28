param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CliArgs
)

# ---- Usage ----
function Show-Usage {
@"
Usage: .\run.ps1 [OPTIONS]

Options:
  -b, --backend        Run .NET API
  -w, --worker         Run .NET Worker
  -a, --all            Build frontend + run API + Worker
  -f, --frontend       Run frontend (npm run dev)

  -n, --npm <args>     Run npm command (client/)
  -d, --dotnet <args>  Run dotnet CLI command

  -k, --kill-port      Kill process on API port
  -h, --help           Show this help
"@ | Write-Host
}

# ---- Flags ----
$Backend = $false
$Worker = $false
$All = $false
$Frontend = $false
$KillPort = $false
$Npm = @()
$Dotnet = @()

# ---- Parse args ----
$i = 0
while ($i -lt $CliArgs.Count) {
    switch ($CliArgs[$i]) {

        "-b" { $Backend = $true }
        "--backend" { $Backend = $true }

        "-w" { $Worker = $true }
        "--worker" { $Worker = $true }

        "-a" { $All = $true }
        "--all" { $All = $true }

        "-f" { $Frontend = $true }
        "--frontend" { $Frontend = $true }

        "-k" { $KillPort = $true }
        "--kill-port" { $KillPort = $true }

        "-h" { Show-Usage; exit }
        "--help" { Show-Usage; exit }

        "-n" { $i++; while ($i -lt $CliArgs.Count -and $CliArgs[$i] -notmatch "^-") { $Npm += $CliArgs[$i]; $i++ }; $i-- }
        "--npm" { $i++; while ($i -lt $CliArgs.Count -and $CliArgs[$i] -notmatch "^-") { $Npm += $CliArgs[$i]; $i++ }; $i-- }

        "-d" { $i++; while ($i -lt $CliArgs.Count -and $CliArgs[$i] -notmatch "^-") { $Dotnet += $CliArgs[$i]; $i++ }; $i-- }
        "--dotnet" { $i++; while ($i -lt $CliArgs.Count -and $CliArgs[$i] -notmatch "^-") { $Dotnet += $CliArgs[$i]; $i++ }; $i-- }

        default {
            Write-Host "Unknown option: $($CliArgs[$i])"
            Show-Usage
            exit 1
        }
    }
    $i++
}

if (-not ($Backend -or $Worker -or $All -or $Frontend -or $KillPort -or $Npm.Count -or $Dotnet.Count)) {
    Show-Usage
    exit
}

# ---- Paths ----
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ClientDir = Join-Path $ScriptDir "client"
$ApiProject = Join-Path $ScriptDir "server/Api/Api.csproj"
$WorkerProject = Join-Path $ScriptDir "server/Worker/Worker.csproj"
$Wwwroot = Join-Path $ScriptDir "server/Api/wwwroot"

$ApiPort = 5000

# ---- Helpers ----

function Free-Port {
    $connections = netstat -ano | Select-String ":$ApiPort"
    foreach ($line in $connections) {
        $parts = ($line -split "\s+") | Where-Object { $_ }
        $pid = $parts[-1]
        if ($pid -match "^\d+$") {
            Write-Host "Killing PID $pid on port $ApiPort"
            taskkill /PID $pid /F | Out-Null
        }
    }
}

function Restore-Dotnet {
    Write-Host "Restoring .NET dependencies..."
    dotnet restore $ApiProject
    dotnet restore $WorkerProject
}

function Build-Frontend {
    Write-Host "Building frontend..."
    npm --prefix $ClientDir install
    npm --prefix $ClientDir run build

    if (!(Test-Path $Wwwroot)) {
        New-Item -ItemType Directory -Path $Wwwroot | Out-Null
    }

    if (Test-Path "$ClientDir/dist") {
        Write-Host "Syncing dist → wwwroot..."
        robocopy "$ClientDir/dist" $Wwwroot /MIR | Out-Null
    }
}

function Run-Backend {
    Write-Host "Running .NET API..."
    dotnet run --project $ApiProject
}

function Run-Worker {
    Write-Host "Running .NET Worker..."
    dotnet run --project $WorkerProject
}

# ---- Execution ----

if ($KillPort) {
    Free-Port
    exit
}

if ($Dotnet.Count -gt 0) {
    dotnet @Dotnet
    exit
}

if ($Npm.Count -gt 0) {
    npm --prefix $ClientDir @Npm
    exit
}

if ($Backend) {
    Free-Port
    Restore-Dotnet
    Run-Backend
    exit
}

if ($Worker) {
    Restore-Dotnet
    Run-Worker
    exit
}

if ($Frontend) {
    npm --prefix $ClientDir install
    npm --prefix $ClientDir run dev
    exit
}

if ($All) {
    Free-Port
    Restore-Dotnet
    Build-Frontend

    Write-Host "Starting API + Worker..."

    $api = Start-Process powershell `
        -ArgumentList "-NoExit", "-Command", "dotnet run --project '$ApiProject'" `
        -PassThru

    $worker = Start-Process powershell `
        -ArgumentList "-NoExit", "-Command", "dotnet run --project '$WorkerProject'" `
        -PassThru

    Write-Host "API PID: $($api.Id)"
    Write-Host "Worker PID: $($worker.Id)"
    Write-Host "Press Enter to stop..."

    [void][Console]::ReadLine()

    try { Stop-Process $api.Id -Force } catch {}
    try { Stop-Process $worker.Id -Force } catch {}
}