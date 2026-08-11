<#
.SYNOPSIS
Key ring naming, generation and validation shared by the provisioning scripts.

.DESCRIPTION
Dot-sourced by Provision-PaymentKeyRing.ps1 and New-PaymentKeyRingSecret.ps1. These live here
rather than in each script because the secret name must match PaymentKeyRingSecretName.Create
exactly: the service computes the name and never looks it up, so two copies of this logic
drifting apart reads as "payments unavailable" with nothing to indicate why.
#>

Set-StrictMode -Version 2.0

$script:PaymentKeyRingSharedSecretName = "PaymentProviderTokenEncryptionKeyRing"
$script:PaymentKeyRingMaximumKeyCount = 16

<#
Mirrors PaymentSlug.Create: keep letters, digits and hyphens up to 40 characters, then append
eight hex characters of the SHA-256 of the original value. The hash is what stops two different
identifiers collapsing onto the same name once the unsafe characters are stripped.
#>
function ConvertTo-PaymentSlug {
    param([Parameter(Mandatory = $true)][string]$Value)

    $readable = New-Object Text.StringBuilder

    foreach ($character in $Value.ToCharArray()) {
        if ($readable.Length -eq 40) { break }

        if (($character -ge 'a' -and $character -le 'z') -or
            ($character -ge 'A' -and $character -le 'Z') -or
            ($character -ge '0' -and $character -le '9') -or
            $character -eq '-') {
            $null = $readable.Append($character)
        }
    }

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    $sha256 = [Security.Cryptography.SHA256]::Create()

    try {
        $hash = $sha256.ComputeHash($bytes)
        $fingerprint = ([BitConverter]::ToString($hash) -replace '-', '').
            ToLowerInvariant().Substring(0, 8)
    }
    finally {
        $sha256.Dispose()
        [Array]::Clear($bytes, 0, $bytes.Length)
    }

    if ($readable.Length -eq 0) {
        return $fingerprint
    }

    return "$($readable.ToString())-$fingerprint"
}

function Get-KeyRingSecretName {
    param(
        [Parameter(Mandatory = $true)][string]$Tenant,
        [AllowNull()][string]$Organization
    )

    $name = "payment-keyring-$(ConvertTo-PaymentSlug $Tenant)"

    if (-not [string]::IsNullOrWhiteSpace($Organization)) {
        $name = "$name-$(ConvertTo-PaymentSlug $Organization)"
    }

    return $name
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

    if ($null -eq $InputObject) { return $null }

    $property = $InputObject.PSObject.Properties[$Name]

    if ($null -eq $property) { return $null }

    return $property.Value
}

function Test-Base64Key {
    param(
        [AllowNull()][string]$Value,
        [int[]]$AllowedLengths = @(16, 24, 32)
    )

    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }

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

function ConvertTo-KeyDictionary {
    param([AllowNull()]$KeysObject)

    $keys = [ordered]@{}

    if ($null -eq $KeysObject) { return $keys }

    foreach ($property in $KeysObject.PSObject.Properties) {
        $keys[$property.Name] = [string]$property.Value
    }

    return $keys
}

function Test-ProviderTokenKeyRing {
    param([AllowNull()]$Secret)

    $activeKeyId = [string](Get-PropertyValue $Secret "activeKeyId")
    $keys = ConvertTo-KeyDictionary (Get-PropertyValue $Secret "keys")

    if ([string]::IsNullOrWhiteSpace($activeKeyId) -or
        $activeKeyId.Length -gt 128 -or
        $keys.Count -lt 1 -or
        $keys.Count -gt $script:PaymentKeyRingMaximumKeyCount -or
        -not $keys.Contains($activeKeyId)) {
        return $false
    }

    foreach ($keyId in $keys.Keys) {
        if ([string]::IsNullOrWhiteSpace([string]$keyId) -or
            ([string]$keyId).Length -gt 128 -or
            -not (Test-Base64Key ([string]$keys[$keyId]))) {
            return $false
        }
    }

    return $true
}

function Assert-KeyId {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ($Value -notmatch '^[A-Za-z0-9._-]{1,128}$') {
        throw "Encryption key ID may contain only letters, numbers, periods, underscores and hyphens, with a maximum length of 128 characters."
    }
}

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
