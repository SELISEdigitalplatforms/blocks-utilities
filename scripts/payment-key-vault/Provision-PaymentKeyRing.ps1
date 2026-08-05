<#
.SYNOPSIS
Creates or rotates the encryption key ring for one tenant and organization.

.DESCRIPTION
Each tenant-and-organization gets its own key ring, because an organization within a tenant may
be a separate business. The secret name is computed from the two identifiers and must match
PaymentKeyRingSecretName.Create exactly - the service never looks the name up, so a mismatch
reads as "payments unavailable" with no indication why.

Key material is generated here and never by the application: the application's vault access is
read-only by design, and anything that can write its own keys can also overwrite them.

.PARAMETER OrganizationId
Omit for the tenant-level ring, which serves records written before organizations existed and
organizations with no configuration of their own. That is its own ring, not a wildcard.

.PARAMETER ImportSharedKey
Seeds a new ring with the pre-migration shared key, present but NOT active. Existing records
still decrypt while new writes use the fresh active key. Run the re-encryption endpoint next,
then remove the shared key with -RemoveKeyId once nothing references it.

.PARAMETER RemoveKeyId
Removes a retired key. Refused unless the key is not the active one. Do this only after
re-encryption reports nothing left to move: any record still naming this key becomes
permanently unreadable.

.EXAMPLE
./Provision-PaymentKeyRing.ps1 -VaultName blocks-keyvault-dev -TenantId acme -OrganizationId retail
#>
[CmdletBinding()]
param(
    [string]$VaultName,
    [string]$TenantId,
    [string]$OrganizationId,
    [string]$KeyId,
    [switch]$ImportSharedKey,
    [string]$RemoveKeyId
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "PaymentKeyRingCommon.ps1")

$SharedSecretName = $script:PaymentKeyRingSharedSecretName
$MaximumKeyCount = $script:PaymentKeyRingMaximumKeyCount

function Invoke-AzureCli {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & az @Arguments 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        $safeOutput = ($output | Out-String).Trim()
        throw "Azure CLI failed with exit code $exitCode. $safeOutput"
    }

    return $output
}

function Test-KeyVaultSecretExists {
    param(
        [Parameter(Mandatory = $true)][string]$Vault,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $null = & az keyvault secret show `
        --vault-name $Vault `
        --name $Name `
        --query id `
        --output tsv `
        --only-show-errors 2>$null

    return $LASTEXITCODE -eq 0
}

function Read-KeyVaultJsonSecret {
    param(
        [Parameter(Mandatory = $true)][string]$Vault,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $temporaryFile = Join-Path `
        ([IO.Path]::GetTempPath()) `
        ("payment-keyring-" + [Guid]::NewGuid().ToString("N") + ".json")

    try {
        $null = Invoke-AzureCli @(
            "keyvault", "secret", "download",
            "--vault-name", $Vault,
            "--name", $Name,
            "--file", $temporaryFile,
            "--encoding", "utf-8",
            "--overwrite",
            "--only-show-errors"
        )

        return [IO.File]::ReadAllText($temporaryFile) |
            ConvertFrom-Json -ErrorAction Stop
    }
    finally {
        Remove-Item -LiteralPath $temporaryFile -Force -ErrorAction SilentlyContinue
    }
}

function Set-KeyVaultJsonSecret {
    param(
        [Parameter(Mandatory = $true)][string]$Vault,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$Value
    )

    $temporaryFile = Join-Path `
        ([IO.Path]::GetTempPath()) `
        ("payment-keyring-" + [Guid]::NewGuid().ToString("N") + ".json")

    try {
        $json = $Value | ConvertTo-Json -Depth 16 -Compress
        $null = $json | ConvertFrom-Json -ErrorAction Stop
        $utf8WithoutBom = New-Object Text.UTF8Encoding($false)
        [IO.File]::WriteAllText($temporaryFile, $json, $utf8WithoutBom)

        $null = Invoke-AzureCli @(
            "keyvault", "secret", "set",
            "--vault-name", $Vault,
            "--name", $Name,
            "--file", $temporaryFile,
            "--encoding", "utf-8",
            "--content-type", "application/json",
            "--only-show-errors"
        )
    }
    finally {
        Remove-Item -LiteralPath $temporaryFile -Force -ErrorAction SilentlyContinue
    }
}

try {
    Write-Heading "Payment encryption key ring provisioning"
    Write-Host "Creates or rotates the key ring for one tenant and organization."
    Write-Host "Key material is never printed and existing keys are never overwritten."

    if ($null -eq (Get-Command az -ErrorAction SilentlyContinue)) {
        throw "Azure CLI was not found. Install Azure CLI and run this script again."
    }

    $null = & az account show --only-show-errors 2>$null

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Azure CLI login is required. Opening Azure login..." -ForegroundColor Yellow
        $null = Invoke-AzureCli @("login", "--only-show-errors")
    }

    if ([string]::IsNullOrWhiteSpace($VaultName)) {
        $VaultName = Read-RequiredText "Azure Key Vault name" "blocks-keyvault-dev"
    }

    if ([string]::IsNullOrWhiteSpace($TenantId)) {
        $TenantId = Read-RequiredText "Payment tenant ID"
    }

    $secretName = Get-KeyRingSecretName $TenantId $OrganizationId

    if ($secretName.Length -gt 127) {
        throw "Computed secret name '$secretName' exceeds Key Vault's 127-character limit."
    }

    $scopeLabel = if ([string]::IsNullOrWhiteSpace($OrganizationId)) {
        "$TenantId (tenant-level)"
    }
    else {
        "$TenantId / $OrganizationId"
    }

    Write-Host ""
    Write-Host "Scope       : $scopeLabel"
    Write-Host "Secret name : $secretName"

    $null = Invoke-AzureCli @(
        "keyvault", "show",
        "--name", $VaultName,
        "--query", "id",
        "--output", "tsv",
        "--only-show-errors"
    )

    Write-Heading "Key ring"
    $exists = Test-KeyVaultSecretExists $VaultName $secretName
    $keys = [ordered]@{}
    $activeKeyId = $null

    if ($exists) {
        try {
            $existing = Read-KeyVaultJsonSecret $VaultName $secretName
        }
        catch {
            throw "Existing secret '$secretName' is not valid JSON. Restore a valid Key Vault version. Never replace a malformed key ring, because existing ciphertext may become unreadable."
        }

        if (-not (Test-ProviderTokenKeyRing $existing)) {
            throw "Existing secret '$secretName' has an invalid key ring schema. Restore a valid version before continuing."
        }

        $activeKeyId = [string](Get-PropertyValue $existing "activeKeyId")
        $keys = ConvertTo-KeyDictionary (Get-PropertyValue $existing "keys")
        Write-Host "Found an existing key ring with $($keys.Count) key(s); active is '$activeKeyId'."
    }

    if (-not [string]::IsNullOrWhiteSpace($RemoveKeyId)) {
        if (-not $exists) {
            throw "There is no key ring at '$secretName' to remove a key from."
        }

        if (-not $keys.Contains($RemoveKeyId)) {
            throw "Key '$RemoveKeyId' is not in this key ring."
        }

        if ([string]::Equals($RemoveKeyId, $activeKeyId, [StringComparison]::Ordinal)) {
            throw "Key '$RemoveKeyId' is the active key and cannot be removed. Add a new active key and re-encrypt first."
        }

        Write-Host ""
        Write-Host "Any record still encrypted under '$RemoveKeyId' becomes permanently unreadable." -ForegroundColor Yellow
        Write-Host "Only continue if the re-encryption endpoint reports nothing left to move for this scope." -ForegroundColor Yellow

        if ((Read-Host "Type REMOVE-KEY to confirm") -cne "REMOVE-KEY") {
            Write-Host "Key removal cancelled."
            exit 0
        }

        $keys.Remove($RemoveKeyId)
    }
    else {
        if ($keys.Count -ge $MaximumKeyCount) {
            throw "This key ring already holds the maximum of $MaximumKeyCount keys. Re-encrypt and remove a retired key before adding another."
        }

        if ($exists -and -not (Read-Confirmation "Add a new active key to this ring?")) {
            Write-Host "Keeping the existing key ring unchanged."
            exit 0
        }

        if ([string]::IsNullOrWhiteSpace($KeyId)) {
            $defaultKeyId = "payment-token-key-$(Get-Date -Format 'yyyy-MM')"

            if ($keys.Contains($defaultKeyId)) {
                $defaultKeyId = "payment-token-key-$(Get-Date -Format 'yyyy-MM-dd-HHmmss')"
            }

            $KeyId = Read-RequiredText "New encryption key ID" $defaultKeyId
        }

        Assert-KeyId $KeyId

        if ($keys.Contains($KeyId)) {
            throw "Encryption key ID '$KeyId' already exists. Existing key material is never overwritten."
        }

        # The shared key goes in present but NOT active, so records written before the
        # migration still decrypt while every new write uses the fresh key.
        if ($ImportSharedKey) {
            if (-not (Test-KeyVaultSecretExists $VaultName $SharedSecretName)) {
                throw "The shared key ring '$SharedSecretName' was not found; there is nothing to import."
            }

            $shared = Read-KeyVaultJsonSecret $VaultName $SharedSecretName

            if (-not (Test-ProviderTokenKeyRing $shared)) {
                throw "The shared key ring '$SharedSecretName' has an invalid schema."
            }

            $sharedKeys = ConvertTo-KeyDictionary (Get-PropertyValue $shared "keys")

            foreach ($sharedKeyId in $sharedKeys.Keys) {
                if (-not $keys.Contains($sharedKeyId)) {
                    $keys[$sharedKeyId] = [string]$sharedKeys[$sharedKeyId]
                }
            }

            Write-Host "Imported $($sharedKeys.Count) shared key(s), none of them active."
        }

        $keys[$KeyId] = New-SecureBase64Key
        $activeKeyId = $KeyId
    }

    $keyRingToStore = [ordered]@{
        activeKeyId = $activeKeyId
        keys = $keys
    }

    $roundTripped = $keyRingToStore |
        ConvertTo-Json -Depth 8 |
        ConvertFrom-Json

    if (-not (Test-ProviderTokenKeyRing $roundTripped)) {
        throw "The generated key ring did not pass local validation; nothing was written."
    }

    Set-KeyVaultJsonSecret $VaultName $secretName $keyRingToStore
    Write-Host "Key ring stored successfully." -ForegroundColor Green

    Write-Heading "Verification"
    $verified = Read-KeyVaultJsonSecret $VaultName $secretName

    if (-not (Test-ProviderTokenKeyRing $verified)) {
        throw "The stored key ring failed read-back validation."
    }

    Write-Host "Verified: $secretName" -ForegroundColor Green
    Write-Host "Active key: $([string](Get-PropertyValue $verified 'activeKeyId'))"

    Write-Heading "Next steps"
    Write-Host "  1. GET  /api/payments/providers/encryption          confirm the ring is readable"
    Write-Host "  2. POST /api/payments/providers/encryption/re-encrypt   move existing ciphertext"
    Write-Host "  3. Re-run step 2 until it reports nothing re-encrypted"
    Write-Host "  4. Remove the retired key with -RemoveKeyId"
    Write-Host ""
    Write-Host "Only once every scope is provisioned and re-encrypted, set" -ForegroundColor Yellow
    Write-Host "Payment:FallBackToSharedEncryptionKeyRing to false." -ForegroundColor Yellow
}
catch {
    Write-Host ""
    Write-Host "Payment key ring provisioning failed:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
