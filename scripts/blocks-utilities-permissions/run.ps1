<#
.SYNOPSIS
    Seeds the blocks-utilities endpoint permissions into every tenant database.

.DESCRIPTION
    Reads connection settings from .env beside this script (see .env.example),
    or from variables already exported in the shell, which win over the file.

    Dry run unless -Apply is passed. The dry run writes nothing: it prints, per
    database, how many permissions would be inserted and a sample document.

    Every run is written to a timestamped transcript in ./logs.

.PARAMETER Apply
    Commit the inserts. Without this, nothing is written.

.PARAMETER OnlyDbs
    Comma-separated tenant databases to restrict the run to. Overrides
    SEED_ONLY_DBS from .env.

.PARAMETER Yes
    Skip the confirmation prompt on -Apply. For non-interactive pipelines.

.EXAMPLE
    .\run.ps1
    Dry run against everything .env points at.

.EXAMPLE
    .\run.ps1 -OnlyDbs "acme_db" -Apply
    Seed one tenant.

.EXAMPLE
    .\run.ps1 -Apply -Yes
    Full rollout, no prompt.
#>
[CmdletBinding()]
param(
    [switch] $Apply,
    [string] $OnlyDbs,
    [switch] $Yes
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$SeedScript = Join-Path $ScriptDir 'seed-permissions.js'
$EnvFile    = Join-Path $ScriptDir '.env'
$LogDir     = Join-Path $ScriptDir 'logs'
$Stamp      = (Get-Date).ToUniversalTime().ToString('yyyyMMdd_HHmmss')
$LogFile    = Join-Path $LogDir "seed_$Stamp.log"

if (-not (Test-Path $SeedScript)) {
    throw "seed-permissions.js not found beside this script ($SeedScript)."
}

# --- settings: .env first, then anything already in the environment ----------

$settings = @{}

if (Test-Path $EnvFile) {
    foreach ($line in Get-Content $EnvFile) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $split = $trimmed.IndexOf('=')
        if ($split -lt 1) { continue }
        $key = $trimmed.Substring(0, $split).Trim()
        $val = $trimmed.Substring($split + 1).Trim()
        if ($val.Length -ge 2 -and
            (($val.StartsWith('"') -and $val.EndsWith('"')) -or
             ($val.StartsWith("'") -and $val.EndsWith("'")))) {
            $val = $val.Substring(1, $val.Length - 2)
        }
        $settings[$key] = $val
    }
    Write-Host "  Settings file   : $EnvFile"
} else {
    Write-Host "  Settings file   : (none - using environment variables)"
}

function Get-Setting([string] $Name, [string] $Default = '') {
    $fromEnv = [Environment]::GetEnvironmentVariable($Name)
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) { return $fromEnv }
    if ($settings.ContainsKey($Name) -and -not [string]::IsNullOrWhiteSpace($settings[$Name])) {
        return $settings[$Name]
    }
    return $Default
}

$MongoUri    = Get-Setting 'BLOCKS_MONGO_URI'
$OnlyDbsRaw  = if ($PSBoundParameters.ContainsKey('OnlyDbs')) { $OnlyDbs } else { Get-Setting 'SEED_ONLY_DBS' }
$OrgId       = Get-Setting 'SEED_ORGANIZATION_ID' 'default'
$CreatedBy   = Get-Setting 'SEED_CREATED_BY'
$NameTpl     = Get-Setting 'SEED_NAME_TEMPLATE' '{db} - {name}'
$RolesRaw    = Get-Setting 'SEED_ROLES'
$DockerImage = Get-Setting 'SEED_DOCKER_IMAGE' 'mongo:7'

if ([string]::IsNullOrWhiteSpace($MongoUri)) {
    Write-Host ''
    Write-Host '  ERROR: BLOCKS_MONGO_URI is not set.' -ForegroundColor Red
    Write-Host ''
    Write-Host '  Copy .env.example to .env and fill in the connection string:'
    Write-Host ''
    Write-Host "      Copy-Item '$ScriptDir\.env.example' '$EnvFile'"
    Write-Host ''
    Write-Host '  Or export it for this session only:'
    Write-Host ''
    Write-Host "      `$env:BLOCKS_MONGO_URI = 'mongodb://user:pass@host:27017/?authSource=admin'"
    Write-Host ''
    exit 1
}

function Split-List([string] $Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return @() }
    return @($Value.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
}

# Hand-rolled rather than ConvertTo-Json: -AsArray is PowerShell 6.2+, and this
# has to run on Windows PowerShell 5.1 too.
function ConvertTo-JsonString([string] $Value) {
    return '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"'
}

function ConvertTo-JsonArray($Items) {
    if ($null -eq $Items -or @($Items).Count -eq 0) { return '[]' }
    return '[' + ((@($Items) | ForEach-Object { ConvertTo-JsonString $_ }) -join ',') + ']'
}

# --- the settings handed to the seeder, as one --eval ------------------------

$evalParts = @()
if ($Apply) { $evalParts += 'SEED_APPLY=true' }
$evalParts += "SEED_ONLY_DBS=$(ConvertTo-JsonArray (Split-List $OnlyDbsRaw))"
$evalParts += "SEED_ROLES=$(ConvertTo-JsonArray (Split-List $RolesRaw))"
$evalParts += "SEED_ORGANIZATION_ID=$(ConvertTo-JsonString $OrgId)"
$evalParts += "SEED_NAME_TEMPLATE=$(ConvertTo-JsonString $NameTpl)"
if (-not [string]::IsNullOrWhiteSpace($CreatedBy)) {
    $evalParts += "SEED_CREATED_BY=$(ConvertTo-JsonString $CreatedBy)"
}
$evalScript = ($evalParts -join '; ')

# --- how we will invoke mongosh ----------------------------------------------

$mongosh = Get-Command mongosh -ErrorAction SilentlyContinue
$useDocker = $null -eq $mongosh

if ($useDocker) {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if ($null -eq $docker) {
        Write-Host ''
        Write-Host '  ERROR: neither mongosh nor docker is available.' -ForegroundColor Red
        Write-Host '  Install MongoDB Shell (https://www.mongodb.com/try/download/shell)'
        Write-Host "  or Docker, which can run it from the $DockerImage image."
        Write-Host ''
        exit 1
    }
}

# @() because an empty array returned from a function unrolls to $null, and
# StrictMode will not let $null.Count be read.
$targets = @(Split-List $OnlyDbsRaw)

Write-Host ''
Write-Host '=========================================='
Write-Host '  blocks-utilities Permission Seeder'
Write-Host '=========================================='
Write-Host "  Mode            : $(if ($Apply) { 'APPLY - inserts will be committed' } else { 'DRY RUN - nothing will be written' })"
Write-Host "  mongosh via     : $(if ($useDocker) { "docker ($DockerImage)" } else { $mongosh.Source })"
Write-Host "  Target tenants  : $(if ($targets.Count -eq 0) { 'every tenant' } else { $targets -join ', ' })"
Write-Host "  OrganizationId  : $OrgId"
Write-Host "  Transcript      : $LogFile"
Write-Host '=========================================='
Write-Host ''

# --- confirm before writing to production ------------------------------------

if ($Apply -and -not $Yes) {
    $scope = if ($targets.Count -eq 0) { 'EVERY tenant database' } else { "$($targets -join ', ')" }
    Write-Host "  About to insert permissions into $scope." -ForegroundColor Yellow
    Write-Host '  Each Permissions collection is backed up first. Existing rows are never modified.' -ForegroundColor Yellow
    Write-Host ''
    $answer = Read-Host '  Type YES to continue'
    if ($answer -ne 'YES') {
        Write-Host '  Aborted.'
        exit 1
    }
    Write-Host ''
}

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

# --- run ----------------------------------------------------------------------

if ($useDocker) {
    $output = & docker run --rm -v "${ScriptDir}:/scripts" $DockerImage `
        mongosh $MongoUri --quiet --eval $evalScript --file /scripts/seed-permissions.js 2>&1
} else {
    $output = & mongosh $MongoUri --quiet --eval $evalScript --file $SeedScript 2>&1
}

$exit = $LASTEXITCODE

$output | ForEach-Object { Write-Host $_ }

# The URI carries a password; keep it out of the transcript.
@(
    "blocks-utilities permission seeder",
    "UTC          : $Stamp",
    "Mode         : $(if ($Apply) { 'APPLY' } else { 'DRY RUN' })",
    "Targets      : $(if ($targets.Count -eq 0) { 'every tenant' } else { $targets -join ', ' })",
    "Runner       : $(if ($useDocker) { "docker $DockerImage" } else { 'mongosh' })",
    "Exit code    : $exit",
    ""
) + $output | Set-Content -Path $LogFile -Encoding UTF8

Write-Host ''
Write-Host "  Transcript written to $LogFile"

if ($exit -ne 0) {
    Write-Host "  mongosh exited with code $exit" -ForegroundColor Red
    exit $exit
}

if (-not $Apply) {
    Write-Host '  Dry run only. Re-run with -Apply to commit.' -ForegroundColor Yellow
}

Write-Host ''
