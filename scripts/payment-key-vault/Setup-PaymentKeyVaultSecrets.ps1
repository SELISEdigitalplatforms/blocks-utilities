[CmdletBinding()]
param(
    [string]$VaultName,
    [string]$TenantId,
    [string]$ProviderSecretName = "payment-adyen-shared"
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Write-Heading {
    param([Parameter(Mandatory = $true)][string]$Text)

    Write-Host ""
    Write-Host $Text -ForegroundColor Cyan
    Write-Host ("-" * $Text.Length) -ForegroundColor DarkCyan
}

function Read-RequiredText {
    param(
        [Parameter(Mandatory = $true)][string]$Prompt,
        [string]$DefaultValue
    )

    while ($true) {
        $displayPrompt = if ([string]::IsNullOrWhiteSpace($DefaultValue)) {
            $Prompt
        }
        else {
            "$Prompt [$DefaultValue]"
        }

        $value = Read-Host $displayPrompt

        if ([string]::IsNullOrWhiteSpace($value)) {
            $value = $DefaultValue
        }

        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }

        Write-Warning "A value is required."
    }
}

function Read-Confirmation {
    param([Parameter(Mandatory = $true)][string]$Prompt)

    $answer = Read-Host "$Prompt [y/N]"

    return $answer -match '^(?i:y|yes)$'
}

function ConvertFrom-SecureText {
    param(
        [Parameter(Mandatory = $true)]
        [Security.SecureString]$SecureValue
    )

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR(
        $SecureValue)

    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
            $pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Read-RequiredSecret {
    param(
        [Parameter(Mandatory = $true)][string]$Prompt,
        [Parameter(Mandatory = $true)][scriptblock]$Validator,
        [Parameter(Mandatory = $true)][string]$ValidationMessage
    )

    while ($true) {
        $secureValue = Read-Host $Prompt -AsSecureString
        $plainValue = ConvertFrom-SecureText $secureValue

        if (& $Validator $plainValue) {
            return $plainValue
        }

        $plainValue = $null
        Write-Warning $ValidationMessage
    }
}

function New-SecureBase64Key {
    $bytes = New-Object byte[] 32
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()

    try {
        $random.GetBytes($bytes)

        return [Convert]::ToBase64String($bytes)
    }
    finally {
        $random.Dispose()
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Get-PropertyValue {
    param(
        [AllowNull()]$InputObject,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $InputObject) {
        return $null
    }

    $property = $InputObject.PSObject.Properties[$Name]

    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Test-HexHmac {
    param([AllowNull()][string]$Value)

    return $null -ne $Value -and
        $Value -match '^[0-9A-Fa-f]{64}$'
}

function Test-Base64Key {
    param(
        [AllowNull()][string]$Value,
        [int[]]$AllowedLengths = @(32)
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    try {
        $bytes = [Convert]::FromBase64String($Value)

        try {
            return $AllowedLengths -contains $bytes.Length
        }
        finally {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
    catch [FormatException] {
        return $false
    }
}

function Test-ProviderCredentialSecret {
    param([AllowNull()]$Secret)

    $standard = Get-PropertyValue $Secret "standardWebhookHmac"
    $token = Get-PropertyValue $Secret "tokenWebhookHmac"
    $apiKey = [string](Get-PropertyValue $Secret "apiKey")
    $standardActive = [string](Get-PropertyValue $standard "active")
    $standardPrevious = [string](Get-PropertyValue $standard "previous")
    $tokenActive = [string](Get-PropertyValue $token "active")
    $tokenPrevious = [string](Get-PropertyValue $token "previous")

    return -not [string]::IsNullOrWhiteSpace($apiKey) -and
        (Test-HexHmac $standardActive) -and
        ([string]::IsNullOrWhiteSpace($standardPrevious) -or
            (Test-HexHmac $standardPrevious)) -and
        (Test-HexHmac $tokenActive) -and
        ([string]::IsNullOrWhiteSpace($tokenPrevious) -or
            (Test-HexHmac $tokenPrevious))
}

function Test-TenantSecuritySecret {
    param([AllowNull()]$Secret)

    $returnState = Get-PropertyValue $Secret "returnStateHmac"
    $active = [string](Get-PropertyValue $returnState "active")
    $previous = [string](Get-PropertyValue $returnState "previous")
    $shopper = [string](Get-PropertyValue $Secret "shopperReferenceHmacKey")

    return (Test-Base64Key $active) -and
        ([string]::IsNullOrWhiteSpace($previous) -or
            (Test-Base64Key $previous)) -and
        (Test-Base64Key $shopper)
}

function ConvertTo-KeyDictionary {
    param([AllowNull()]$KeysObject)

    $keys = [ordered]@{}

    if ($null -eq $KeysObject) {
        return $keys
    }

    foreach ($property in $KeysObject.PSObject.Properties) {
        $keys[$property.Name] = [string]$property.Value
    }

    return $keys
}

function Test-ProviderTokenKeyRing {
    param([AllowNull()]$Secret)

    $activeKeyId = [string](Get-PropertyValue $Secret "activeKeyId")
    $keysObject = Get-PropertyValue $Secret "keys"
    $keys = ConvertTo-KeyDictionary $keysObject

    if ([string]::IsNullOrWhiteSpace($activeKeyId) -or
        $activeKeyId.Length -gt 128 -or
        $keys.Count -lt 1 -or
        $keys.Count -gt 16 -or
        -not $keys.Contains($activeKeyId)) {
        return $false
    }

    foreach ($keyId in $keys.Keys) {
        if ([string]::IsNullOrWhiteSpace([string]$keyId) -or
            ([string]$keyId).Length -gt 128 -or
            -not (Test-Base64Key `
                ([string]$keys[$keyId]) `
                @(16, 24, 32))) {
            return $false
        }
    }

    return $true
}

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
        ("payment-secret-" + [Guid]::NewGuid().ToString("N") + ".json")

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

        $json = [IO.File]::ReadAllText($temporaryFile)

        return $json | ConvertFrom-Json -ErrorAction Stop
    }
    finally {
        Remove-Item `
            -LiteralPath $temporaryFile `
            -Force `
            -ErrorAction SilentlyContinue
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
        ("payment-secret-" + [Guid]::NewGuid().ToString("N") + ".json")

    try {
        $json = $Value | ConvertTo-Json -Depth 16 -Compress
        $null = $json | ConvertFrom-Json -ErrorAction Stop
        $utf8WithoutBom = New-Object Text.UTF8Encoding($false)
        [IO.File]::WriteAllText(
            $temporaryFile,
            $json,
            $utf8WithoutBom)

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
        Remove-Item `
            -LiteralPath $temporaryFile `
            -Force `
            -ErrorAction SilentlyContinue
    }
}

function Assert-SecretName {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if ($Name -notmatch '^[A-Za-z0-9-]{1,127}$') {
        throw "$Description must contain only letters, numbers and hyphens, with a maximum length of 127 characters."
    }
}

function Get-RotatedPreviousValue {
    param(
        [Parameter(Mandatory = $true)][string]$NewActive,
        [Parameter(Mandatory = $true)][string]$OldActive,
        [AllowNull()][string]$OldPrevious
    )

    if ([string]::Equals(
            $NewActive,
            $OldActive,
            [StringComparison]::OrdinalIgnoreCase)) {
        if ([string]::IsNullOrWhiteSpace($OldPrevious)) {
            return $null
        }

        return $OldPrevious
    }

    return $OldActive
}

try {
    Write-Heading "Payment Key Vault setup"
    Write-Host "This script creates or safely rotates payment-specific Key Vault secrets."
    Write-Host "Secret values are never printed. Existing malformed secrets are not overwritten."
    Write-Host ""
    Write-Host "Before continuing, obtain these values from Adyen Customer Area:"
    Write-Host "  1. Checkout API key"
    Write-Host "  2. Standard webhook HMAC key (64 hexadecimal characters)"
    Write-Host "  3. Token webhook HMAC key (64 hexadecimal characters)"

    if ($null -eq (Get-Command az -ErrorAction SilentlyContinue)) {
        throw "Azure CLI was not found. Install Azure CLI and run this script again."
    }

    $null = & az account show --only-show-errors 2>$null

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Azure CLI login is required. Opening Azure login..." -ForegroundColor Yellow
        $null = Invoke-AzureCli @("login", "--only-show-errors")
    }

    if ([string]::IsNullOrWhiteSpace($VaultName)) {
        $VaultName = Read-RequiredText `
            "Azure Key Vault name" `
            "blocks-keyvault-dev"
    }

    if ([string]::IsNullOrWhiteSpace($TenantId)) {
        $TenantId = Read-RequiredText "Payment tenant ID"
    }

    if ($TenantId -notmatch '^[A-Za-z0-9-]{1,80}$') {
        throw "Tenant ID must contain only letters, numbers and hyphens, with a maximum length of 80 characters."
    }

    $TenantSecretName = "payment-tenant-$TenantId"
    $KeyRingSecretName = "PaymentProviderTokenEncryptionKeyRing"

    Assert-SecretName $ProviderSecretName "Provider secret name"
    Assert-SecretName $TenantSecretName "Tenant security secret name"
    Assert-SecretName $KeyRingSecretName "Provider-token keyring secret name"

    $null = Invoke-AzureCli @(
        "keyvault", "show",
        "--name", $VaultName,
        "--query", "id",
        "--output", "tsv",
        "--only-show-errors"
    )

    Write-Heading "Adyen provider credentials"
    $providerExists = Test-KeyVaultSecretExists `
        $VaultName `
        $ProviderSecretName
    $providerSecret = $null
    $writeProvider = $true

    if ($providerExists) {
        try {
            $providerSecret = Read-KeyVaultJsonSecret `
                $VaultName `
                $ProviderSecretName
        }
        catch {
            throw "Existing secret '$ProviderSecretName' is not valid JSON. Restore a valid Key Vault version before running this script."
        }

        if (-not (Test-ProviderCredentialSecret $providerSecret)) {
            throw "Existing secret '$ProviderSecretName' has an invalid payment-provider schema. Restore or repair it before running this script."
        }

        if (-not (Read-Confirmation "Update the existing Adyen API key or webhook HMAC keys?")) {
            $writeProvider = $false
            Write-Host "Keeping existing Adyen provider credentials."
        }
    }

    if ($writeProvider) {
        $apiKey = Read-RequiredSecret `
            "Paste the Adyen Checkout API key" `
            { param($value) -not [string]::IsNullOrWhiteSpace($value) -and $value.Length -le 8192 } `
            "The API key cannot be empty."
        $standardHmac = Read-RequiredSecret `
            "Paste the Adyen standard-webhook HMAC key" `
            { param($value) Test-HexHmac $value } `
            "The standard-webhook HMAC must contain exactly 64 hexadecimal characters."
        $tokenHmac = Read-RequiredSecret `
            "Paste the Adyen token-webhook HMAC key" `
            { param($value) Test-HexHmac $value } `
            "The token-webhook HMAC must contain exactly 64 hexadecimal characters."

        $standardHmac = $standardHmac.ToUpperInvariant()
        $tokenHmac = $tokenHmac.ToUpperInvariant()
        $previousStandard = $null
        $previousToken = $null

        if ($null -ne $providerSecret) {
            $oldStandard = Get-PropertyValue `
                $providerSecret `
                "standardWebhookHmac"
            $oldToken = Get-PropertyValue `
                $providerSecret `
                "tokenWebhookHmac"
            $previousStandard = Get-RotatedPreviousValue `
                $standardHmac `
                ([string](Get-PropertyValue $oldStandard "active")) `
                ([string](Get-PropertyValue $oldStandard "previous"))
            $previousToken = Get-RotatedPreviousValue `
                $tokenHmac `
                ([string](Get-PropertyValue $oldToken "active")) `
                ([string](Get-PropertyValue $oldToken "previous"))
        }

        $providerSecretToStore = [ordered]@{
            apiKey = $apiKey
            standardWebhookHmac = [ordered]@{
                active = $standardHmac
                previous = $previousStandard
            }
            tokenWebhookHmac = [ordered]@{
                active = $tokenHmac
                previous = $previousToken
            }
        }

        if (-not (Test-ProviderCredentialSecret `
                ($providerSecretToStore |
                    ConvertTo-Json -Depth 8 |
                    ConvertFrom-Json))) {
            throw "Generated provider credential JSON did not pass local validation."
        }

        Set-KeyVaultJsonSecret `
            $VaultName `
            $ProviderSecretName `
            $providerSecretToStore
        Write-Host "Adyen provider credential secret stored successfully." -ForegroundColor Green
        $apiKey = $null
        $standardHmac = $null
        $tokenHmac = $null
    }

    Write-Heading "Tenant payment security"
    $tenantExists = Test-KeyVaultSecretExists `
        $VaultName `
        $TenantSecretName
    $tenantSecret = $null
    $writeTenant = $true

    if ($tenantExists) {
        try {
            $tenantSecret = Read-KeyVaultJsonSecret `
                $VaultName `
                $TenantSecretName
        }
        catch {
            throw "Existing secret '$TenantSecretName' is not valid JSON. Restore a valid Key Vault version before running this script."
        }

        if (-not (Test-TenantSecuritySecret $tenantSecret)) {
            throw "Existing secret '$TenantSecretName' has an invalid tenant-security schema. Restore or repair it before running this script."
        }

        $returnState = Get-PropertyValue $tenantSecret "returnStateHmac"
        $returnActive = [string](Get-PropertyValue $returnState "active")
        $returnPrevious = [string](Get-PropertyValue $returnState "previous")
        $shopperKey = [string](Get-PropertyValue $tenantSecret "shopperReferenceHmacKey")
        $rotateReturn = Read-Confirmation "Rotate the return-state HMAC key?"
        $rotateShopper = $false

        Write-Host "Changing the shopper-reference key changes shopper identities and can hide existing stored methods." -ForegroundColor Yellow
        $shopperConfirmation = Read-Host "Type ROTATE-SHOPPER only if a coordinated migration is ready; otherwise press Enter"

        if ($shopperConfirmation -ceq "ROTATE-SHOPPER") {
            $rotateShopper = $true
        }

        if ($rotateReturn) {
            $returnPrevious = $returnActive
            $returnActive = New-SecureBase64Key
        }

        if ($rotateShopper) {
            $shopperKey = New-SecureBase64Key
        }

        if (-not $rotateReturn -and -not $rotateShopper) {
            $writeTenant = $false
            Write-Host "Keeping existing tenant payment-security keys."
        }
    }
    else {
        $returnActive = New-SecureBase64Key
        $returnPrevious = $null
        $shopperKey = New-SecureBase64Key
    }

    if ($writeTenant) {
        $tenantSecretToStore = [ordered]@{
            returnStateHmac = [ordered]@{
                active = $returnActive
                previous = if ([string]::IsNullOrWhiteSpace($returnPrevious)) {
                    $null
                }
                else {
                    $returnPrevious
                }
            }
            shopperReferenceHmacKey = $shopperKey
        }

        Set-KeyVaultJsonSecret `
            $VaultName `
            $TenantSecretName `
            $tenantSecretToStore
        Write-Host "Tenant payment-security secret stored successfully." -ForegroundColor Green
        $returnActive = $null
        $returnPrevious = $null
        $shopperKey = $null
    }

    Write-Heading "Provider-token encryption keyring"
    $keyRingExists = Test-KeyVaultSecretExists `
        $VaultName `
        $KeyRingSecretName
    $writeKeyRing = $true

    if ($keyRingExists) {
        try {
            $keyRingSecret = Read-KeyVaultJsonSecret `
                $VaultName `
                $KeyRingSecretName
        }
        catch {
            throw "Existing secret '$KeyRingSecretName' is not valid JSON. Restore a valid Key Vault version. Never replace a malformed keyring because existing tokens may become undecryptable."
        }

        if (-not (Test-ProviderTokenKeyRing $keyRingSecret)) {
            throw "Existing secret '$KeyRingSecretName' has an invalid keyring schema. Restore a valid version before continuing."
        }

        $activeKeyId = [string](Get-PropertyValue $keyRingSecret "activeKeyId")
        $keys = ConvertTo-KeyDictionary `
            (Get-PropertyValue $keyRingSecret "keys")

        if (Read-Confirmation "Add a new active provider-token encryption key?") {
            if ($keys.Count -ge 16) {
                throw "The encryption keyring already contains the maximum of 16 keys. Perform a controlled token re-encryption migration before removing an old key."
            }

            $defaultKeyId = "payment-token-key-$(Get-Date -Format 'yyyy-MM')"

            if ($keys.Contains($defaultKeyId)) {
                $defaultKeyId = "payment-token-key-$(Get-Date -Format 'yyyy-MM-dd-HHmmss')"
            }

            $newKeyId = Read-RequiredText `
                "New encryption key ID" `
                $defaultKeyId

            if ($newKeyId -notmatch '^[A-Za-z0-9._-]{1,128}$') {
                throw "Encryption key ID may contain only letters, numbers, periods, underscores and hyphens, with a maximum length of 128 characters."
            }

            if ($keys.Contains($newKeyId)) {
                throw "Encryption key ID '$newKeyId' already exists. Existing encryption key material will never be overwritten."
            }

            $keys[$newKeyId] = New-SecureBase64Key
            $activeKeyId = $newKeyId
        }
        else {
            $writeKeyRing = $false
            Write-Host "Keeping existing provider-token encryption keyring."
        }
    }
    else {
        $defaultKeyId = "payment-token-key-$(Get-Date -Format 'yyyy-MM')"
        $activeKeyId = Read-RequiredText `
            "Initial encryption key ID" `
            $defaultKeyId

        if ($activeKeyId -notmatch '^[A-Za-z0-9._-]{1,128}$') {
            throw "Encryption key ID may contain only letters, numbers, periods, underscores and hyphens, with a maximum length of 128 characters."
        }

        $keys = [ordered]@{}
        $keys[$activeKeyId] = New-SecureBase64Key
    }

    if ($writeKeyRing) {
        $keyRingToStore = [ordered]@{
            activeKeyId = $activeKeyId
            keys = $keys
        }

        Set-KeyVaultJsonSecret `
            $VaultName `
            $KeyRingSecretName `
            $keyRingToStore
        Write-Host "Provider-token encryption keyring stored successfully." -ForegroundColor Green
    }

    Write-Heading "Verification"
    $secretNames = @(
        $ProviderSecretName,
        $TenantSecretName,
        $KeyRingSecretName
    )

    foreach ($secretName in $secretNames) {
        if (-not (Test-KeyVaultSecretExists $VaultName $secretName)) {
            throw "Verification failed: Key Vault secret '$secretName' was not found."
        }

        Write-Host "Verified: $secretName" -ForegroundColor Green
    }

    $verifiedProvider = Read-KeyVaultJsonSecret `
        $VaultName `
        $ProviderSecretName
    $verifiedTenant = Read-KeyVaultJsonSecret `
        $VaultName `
        $TenantSecretName
    $verifiedKeyRing = Read-KeyVaultJsonSecret `
        $VaultName `
        $KeyRingSecretName

    if (-not (Test-ProviderCredentialSecret $verifiedProvider) -or
        -not (Test-TenantSecuritySecret $verifiedTenant) -or
        -not (Test-ProviderTokenKeyRing $verifiedKeyRing)) {
        throw "One or more stored secrets failed read-back schema validation."
    }

    Write-Host "All payment secrets passed read-back schema validation." -ForegroundColor Green

    Write-Heading "PaymentProvider database references"
    Write-Host "Set these fields on the tenant's PaymentProvider document:"
    Write-Host "  ProviderCredentialSecretName : $ProviderSecretName"
    Write-Host "  TenantSecuritySecretName     : $TenantSecretName"
    Write-Host ""
    Write-Host "Do not put ApiKey, webhook HMAC keys, return-state keys, shopper-reference keys, or token-encryption keys in MongoDB." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Payment Key Vault setup completed successfully." -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "Payment Key Vault setup failed:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
