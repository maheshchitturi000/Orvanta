# Local setup: generates random development keys and stores them with
# `dotnet user-secrets` (in your Windows profile, never in the repo).
# Safe to re-run: keys that already exist are kept, so existing local accounts keep working.
# Usage:  powershell -ExecutionPolicy Bypass -File .\Microservices\setup-secrets.ps1

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$auth = Join-Path $root 'src/Services/Orvanta.AuthService'
$food = Join-Path $root 'src/Services/Orvanta.FoodService'
$jobs = Join-Path $root 'src/Services/Jobs/Orvanta.JobsService'   # shares its store with Orvanta.Jobs.Functions
$conversation = Join-Path $root 'src/Services/Orvanta.ConversationService'
$ai = Join-Path $root 'src/Services/Orvanta.AIService'

function New-Key([int]$bytes) {
    $buffer = New-Object byte[] $bytes
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
    [Convert]::ToBase64String($buffer)
}

function Get-Secret([string]$project, [string]$name) {
    $line = dotnet user-secrets list --project $project 2>$null | Where-Object { $_ -like "$name = *" } | Select-Object -First 1
    if ($line) { $line.Substring($name.Length + 3) } else { $null }
}

# Sets the secret only when the project doesn't have one yet.
function Set-SecretIfMissing([string]$project, [string]$name, [string]$value) {
    if (-not (Get-Secret $project $name)) {
        dotnet user-secrets set $name $value --project $project | Out-Null
    }
}

# The JWT keys must be identical in every service that issues or validates tokens.
$signingKey = Get-Secret $auth 'Jwt:SigningKey'
if (-not $signingKey) { $signingKey = New-Key 64 }
$encryptionKey = Get-Secret $auth 'Jwt:EncryptionKey'
if (-not $encryptionKey) { $encryptionKey = New-Key 32 }

foreach ($project in $auth, $food, $jobs, $conversation, $ai) {
    dotnet user-secrets set 'Jwt:SigningKey' $signingKey --project $project | Out-Null
    dotnet user-secrets set 'Jwt:EncryptionKey' $encryptionKey --project $project | Out-Null
}

Set-SecretIfMissing $auth 'AuthSecurity:PasswordPepper' (New-Key 32)
Set-SecretIfMissing $auth 'AuthSecurity:OtpHmacKey' (New-Key 32)
Set-SecretIfMissing $food 'DataProtection:Key' (New-Key 32)
Set-SecretIfMissing $jobs 'Jobs:SecretsKey' (New-Key 32)

Write-Host 'Development secrets are in place.'
