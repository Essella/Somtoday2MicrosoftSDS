# Development guide

## Current solution structure

**Code-observed fact:** The solution contains one .NET 10 executable project and one xUnit test project.

## Local configuration

Install the .NET 10 SDK. Copy the ignored Development example and store the Somtoday secret with [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0):

```powershell
Copy-Item Somtoday2MicrosoftSDS/appsettings.Development.example.json Somtoday2MicrosoftSDS/appsettings.Development.json
dotnet user-secrets set --project Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj 'Somtoday:ClientSecret' '<secret>'
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj
```

The example uses `UseDevelopmentStorage=true` for Azurite. Start Azurite or place a Development-only connection string in the ignored file. A configured service URI uses `DefaultAzureCredential` and takes precedence.

## Build and test

```powershell
dotnet restore Somtoday2MicrosoftSDS.sln
dotnet test Somtoday2MicrosoftSDS.sln --configuration Release
dotnet publish Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj --configuration Release --runtime linux-x64 --self-contained false
```

If Azure CLI with Bicep is installed:

```powershell
az bicep build --file infra/main.bicep
```

`infra/main.bicep` is authoritative. Do not edit `infra/azuredeploy.json` manually.

## Local Linux-container smoke test on Windows

[WSL Containers](https://learn.microsoft.com/en-us/windows/wsl/wsl-container) can build and run the Linux image without Docker Desktop:

```powershell
wslc version
.\scripts\Test-ContainerWsl.ps1
```

WSL Containers is currently a public preview and builds for the native host architecture. Use `-ExpectedArchitecture` when needed. If unavailable, update WSL with `wsl --update --pre-release` or use Docker:

```powershell
docker build --tag somtoday2microsoftsds:local .
```

The release workflow's Ubuntu runner performs the current `linux/amd64` image build. A native-architecture local smoke test does not replace that release check.

## Generated Somtoday client

Do not change `Somtoday2MicrosoftSDS/OpenAPIs/openapi.json` or generated `openapi.cs` without explicit scope. Record the source, specification version, and generation tool/version. Never hand-edit generated output when regeneration is available.

## Change validation

Add or update tests for behavioral changes. Preserve cancellation for HTTP, retry, Key Vault, and Blob operations. Keep logs and test data free of secrets, authentication bodies, personal data, and production CSV content. Report tests not run and areas not inspected.
