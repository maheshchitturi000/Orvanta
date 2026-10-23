# Starts every Orvanta service in its own window (local development).
# Usage:  powershell -ExecutionPolicy Bypass -File .\Microservices\run-all.ps1
#
# Requirements: .NET 10 SDK, SQL Server Express, Azure Functions Core Tools 4.x on PATH
# (winget install Microsoft.Azure.FunctionsCoreTools), Azurite (bundled with Visual Studio or `npm i -g azurite`).

$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$services = @(
    @{ Name = 'AuthService (5101)';         Path = 'src/Services/Orvanta.AuthService' },
    @{ Name = 'FoodService (5077)';         Path = 'src/Services/Orvanta.FoodService' },
    @{ Name = 'JobsService (5081)';         Path = 'src/Services/Jobs/Orvanta.JobsService' },
    @{ Name = 'AIService (5212)';           Path = 'src/Services/Orvanta.AIService' },
    @{ Name = 'ConversationService (5005)'; Path = 'src/Services/Orvanta.ConversationService' },
    @{ Name = 'Gateway (5095)';             Path = 'src/Gateway/Orvanta.Gateway' }
)

# Databases first: tables, functions and stored procedures (idempotent, see Database/README.md).
& (Join-Path $root 'Database/Deploy-Database.ps1')

dotnet build (Join-Path $root 'Orvanta.slnx')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

# Azurite: local Azure Storage (queues for the job workflow).
$azurite = (Get-Command azurite -ErrorAction SilentlyContinue).Source
if (-not $azurite) {
    $azurite = Get-ChildItem 'C:\Program Files\Microsoft Visual Studio' -Recurse -Filter azurite.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
}
if ($azurite) {
    $data = Join-Path $env:LOCALAPPDATA 'Orvanta\azurite'
    New-Item -ItemType Directory -Force $data | Out-Null
    Start-Process $azurite -ArgumentList "--silent --skipApiVersionCheck --location `"$data`"" -WindowStyle Minimized
} else {
    Write-Warning 'Azurite not found. Install it (npm i -g azurite) to run the job workflow.'
}

foreach ($service in $services) {
    $project = Join-Path $root $service.Path
    Start-Process powershell -ArgumentList '-NoExit', '-Command',
        "`$Host.UI.RawUI.WindowTitle = 'Orvanta $($service.Name)'; dotnet run --no-build --project '$project' --launch-profile http"
}

# Azure Functions: daily job discovery + apply workers.
$func = (Get-Command func -ErrorAction SilentlyContinue).Source
$functions = Join-Path $root 'src/Services/Jobs/Orvanta.Jobs.Functions'
if ($func) {
    Start-Process powershell -WorkingDirectory $functions -ArgumentList '-NoExit', '-Command',
        "`$Host.UI.RawUI.WindowTitle = 'Orvanta Jobs Functions'; func start --port 7071"
} else {
    Write-Warning 'Azure Functions Core Tools (func) not found. Install with: winget install Microsoft.Azure.FunctionsCoreTools'
}

Write-Host 'Started. Gateway: http://localhost:5095  ·  Angular: npm start (http://localhost:4200)'
