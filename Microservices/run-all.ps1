# Starts every Orvanta microservice in its own window (local development).
# Usage:  powershell -ExecutionPolicy Bypass -File .\Microservices\run-all.ps1

$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$services = @(
    @{ Name = 'AuthService (5101)';         Path = 'src/Services/Orvanta.AuthService' },
    @{ Name = 'FoodService (5077)';         Path = 'src/Services/Orvanta.FoodService' },
    @{ Name = 'AIService (5212)';           Path = 'src/Services/Orvanta.AIService' },
    @{ Name = 'ConversationService (5005)'; Path = 'src/Services/Orvanta.ConversationService' },
    @{ Name = 'Gateway (5095)';             Path = 'src/Gateway/Orvanta.Gateway' }
)

dotnet build (Join-Path $root 'Orvanta.slnx')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

foreach ($service in $services) {
    $project = Join-Path $root $service.Path
    Start-Process powershell -ArgumentList '-NoExit', '-Command',
        "`$Host.UI.RawUI.WindowTitle = 'Orvanta $($service.Name)'; dotnet run --no-build --project '$project' --launch-profile http"
}

Write-Host 'Started. Gateway: http://localhost:5095  ·  Angular: npm start (http://localhost:4200)'
