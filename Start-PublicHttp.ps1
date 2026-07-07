param(
    [int]$Port = 5028
)

$ErrorActionPreference = "Stop"

$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://0.0.0.0:$Port"
$env:Security__UseHttpsRedirection = "false"

dotnet run --no-build --no-restore --no-launch-profile --project .\TechSupportRagBot\TechSupportRagBot.csproj --urls "http://0.0.0.0:$Port"
