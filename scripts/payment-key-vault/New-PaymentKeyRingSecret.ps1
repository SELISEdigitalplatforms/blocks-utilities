<#
.SYNOPSIS
Generates a payment encryption key ring offline, for someone else to store in Key Vault.

.DESCRIPTION
For the case where whoever needs the ring provisioned has no write access to the vault -
production, usually. This generates the key material and computes the secret name locally,
with no Azure access of any kind, and writes the JSON to a file you hand to whoever does have
access. Use Provision-PaymentKeyRing.ps1 instead whenever you can reach the vault yourself:
it writes and reads back, which is one fewer pair of hands on live key material.

The generated file IS the production secret. Treat it as such: hand it over through a secrets
manager or a password manager's secure-share, never chat, email or a ticket, and delete it
once it is stored. Anyone who sees this file can decrypt every payment credential and stored
card in its scope.

Written outside the repository by default, and it must stay outside: a key ring committed to
git is a key ring in every clone and every CI cache, forever.

.PARAMETER TenantId
The tenant the ring belongs to. Case-sensitive - it is hashed into the secret name.

.PARAMETER OrganizationId
Omit for the tenant-level ring. This is the value the *caller's* context carries, which in
practice is usually not blank; a tenant-level ring alone does not satisfy an organization-
scoped caller. Confirm with GET /api/payments/providers/encryption, which reports the exact
secret name the service is looking for.

.PARAMETER OutputPath
Where to write the JSON. Defaults to a file named after the secret in your user profile.

.EXAMPLE
./New-PaymentKeyRingSecret.ps1 -TenantId T33fc... -OrganizationId default
#>
[CmdletBinding()]
param(
    [string]$TenantId,
    [string]$OrganizationId,
    [string]$KeyId,
    [string]$OutputPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "PaymentKeyRingCommon.ps1")

try {
    Write-Heading "Payment encryption key ring - offline generation"
    Write-Host "Generates key material locally. Nothing is sent anywhere and no Azure access is used."

    if ([string]::IsNullOrWhiteSpace($TenantId)) {
        $TenantId = Read-RequiredText "Payment tenant ID"
    }

    if (-not $PSBoundParameters.ContainsKey("OrganizationId")) {
        Write-Host ""
        Write-Host "Organization ID as the service sees it. Leave blank only for the tenant-level ring." -ForegroundColor Yellow
        Write-Host "GET /api/payments/providers/encryption reports the name the service expects." -ForegroundColor Yellow
        $OrganizationId = Read-Host "Organization ID (blank for tenant-level)"
    }

    $secretName = Get-KeyRingSecretName $TenantId $OrganizationId

    if ($secretName.Length -gt 127) {
        throw "Computed secret name '$secretName' exceeds Key Vault's 127-character limit."
    }

    if ([string]::IsNullOrWhiteSpace($KeyId)) {
        $KeyId = Read-RequiredText `
            "Encryption key ID" `
            "payment-token-key-$(Get-Date -Format 'yyyy-MM')"
    }

    Assert-KeyId $KeyId

    $keyRing = [ordered]@{
        activeKeyId = $KeyId
        keys = [ordered]@{ $KeyId = New-SecureBase64Key }
    }

    $json = $keyRing | ConvertTo-Json -Depth 8 -Compress
    $roundTripped = $json | ConvertFrom-Json

    if (-not (Test-ProviderTokenKeyRing $roundTripped)) {
        throw "The generated key ring did not pass local validation; nothing was written."
    }

    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $OutputPath = Join-Path $env:USERPROFILE "$secretName.json"
    }

    $resolvedDirectory = Split-Path -Parent $OutputPath

    if ([string]::IsNullOrWhiteSpace($resolvedDirectory)) {
        $resolvedDirectory = (Get-Location).Path
        $OutputPath = Join-Path $resolvedDirectory $OutputPath
    }

    if (-not (Test-Path -LiteralPath $resolvedDirectory)) {
        throw "The output directory '$resolvedDirectory' does not exist."
    }

    # A key ring inside the working tree is one `git add .` away from being public forever.
    $repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
    $fullOutputPath = [IO.Path]::GetFullPath($OutputPath)

    if ($fullOutputPath.StartsWith(
            $repositoryRoot.Path,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write key material inside the repository at '$fullOutputPath'. Choose a path outside the working tree."
    }

    if (Test-Path -LiteralPath $fullOutputPath) {
        throw "'$fullOutputPath' already exists. Existing key material is never overwritten."
    }

    $utf8WithoutBom = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($fullOutputPath, $json, $utf8WithoutBom)

    Write-Heading "Generated"
    Write-Host "Secret name : $secretName"
    Write-Host "Key ID      : $KeyId"
    Write-Host "Key length  : 32 bytes (AES-256), base64"
    Write-Host "Written to  : $fullOutputPath"
    Write-Host ""
    Write-Host "Key material is not printed. It is only in that file." -ForegroundColor Yellow

    Write-Heading "Hand this to whoever has vault write access"
    Write-Host "Ask them to run, against the production vault:"
    Write-Host ""
    Write-Host "  az keyvault secret set ``"
    Write-Host "    --vault-name <production-vault> ``"
    Write-Host "    --name $secretName ``"
    Write-Host "    --file <path-to-the-file> ``"
    Write-Host "    --encoding utf-8 ``"
    Write-Host "    --content-type application/json"
    Write-Host ""
    Write-Host "The secret name must be exactly as above. The service computes it and never"
    Write-Host "looks it up, so a renamed secret reads as an unexplained 'payments unavailable'."

    Write-Heading "Then"
    Write-Host "  1. Confirm with GET /api/payments/providers/encryption - isReadable must be true"
    Write-Host "  2. Register the provider only after that returns cleanly"
    Write-Host "  3. Delete your local copy: Remove-Item -LiteralPath '$fullOutputPath'"
    Write-Host ""
    Write-Host "Send the file through a secrets manager or password-manager secure share." -ForegroundColor Yellow
    Write-Host "Not chat, not email, not a ticket attachment." -ForegroundColor Yellow
}
catch {
    Write-Host ""
    Write-Host "Key ring generation failed:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
