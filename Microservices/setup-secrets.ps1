# One-time local setup: generates random development keys and stores them with
# `dotnet user-secrets` (in your Windows profile, never in the repo).
# Usage:  powershell -ExecutionPolicy Bypass -File .\Microservices\setup-secrets.ps1

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$auth = Join-Path $root 'src/Services/Orvanta.AuthService'
$food = Join-Path $root 'src/Services/Orvanta.FoodService'

function New-Key([int]$bytes) {
    $buffer = New-Object byte[] $bytes
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
    [Convert]::ToBase64String($buffer)
}

# The JWT keys must be identical in every service that issues or validates tokens.
$signingKey = New-Key 64
$encryptionKey = New-Key 32

foreach ($project in $auth, $food) {
    dotnet user-secrets set 'Jwt:SigningKey' $signingKey --project $project | Out-Null
    dotnet user-secrets set 'Jwt:EncryptionKey' $encryptionKey --project $project | Out-Null
}

dotnet user-secrets set 'AuthSecurity:PasswordPepper' (New-Key 32) --project $auth | Out-Null
dotnet user-secrets set 'AuthSecurity:OtpHmacKey' (New-Key 32) --project $auth | Out-Null
dotnet user-secrets set 'DataProtection:Key' (New-Key 32) --project $food | Out-Null

# Jobs API + Jobs Functions share one user-secrets store.
$jobs = Join-Path $root 'src/Services/Jobs/Orvanta.JobsService'
dotnet user-secrets set 'Jwt:SigningKey' $signingKey --project $jobs | Out-Null
dotnet user-secrets set 'Jwt:EncryptionKey' $encryptionKey --project $jobs | Out-Null
dotnet user-secrets set 'Jobs:SecretsKey' (New-Key 32) --project $jobs | Out-Null

Write-Host 'Development secrets created. Note: re-running this changes the pepper, so existing local accounts must re-register.'
