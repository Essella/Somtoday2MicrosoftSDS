# Development guide

The solution contains one .NET 10 executable and one xUnit test project.

## Local configuration

Copy `Somtoday2MicrosoftSDS/appsettings.Development.example.json` to the ignored `appsettings.Development.json`, replace the example UUIDs, and store the Somtoday secret with User Secrets:

```powershell
dotnet user-secrets set --project Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj 'Somtoday:ClientSecret' '<secret>'
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj
```

`DefaultAzureCredential` must obtain a Microsoft Graph token for local runs. The development identity needs `IndustryData-InboundFlow.ReadWrite.All`, `IndustryData-DataConnector.Upload`, and `IndustryData.ReadBasic.All` application access to the target tenant. No Azurite or local storage emulator is used.

## Build and test

```powershell
dotnet restore Somtoday2MicrosoftSDS.sln
dotnet test Somtoday2MicrosoftSDS.sln --configuration Release
dotnet publish Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj --configuration Release --runtime linux-x64 --self-contained false
./scripts/validate-infrastructure.ps1
```

The infrastructure validation requires Azure CLI with Bicep support and network access to the versioned Azure Form View schema. It compiles the main template and both example parameter files, compares the result with `infra/azuredeploy.json`, validates `infra/uiFormDefinition.json`, and checks that the form outputs exactly match the ARM parameters.

Transport tests use in-memory HTTP handlers and synthetic CSV content. They verify Graph endpoint composition, connector format selection, exact SAS query retention, required PUT headers, bearer-token separation, retries, validation polling, and failure boundaries without contacting Azure or SDS.

Live Somtoday tests remain opt-in through `scripts/Test-SomtodayOpenApi.ps1`; normal test runs skip them. The Somtoday OpenAPI files are generated artifacts and may be changed only under the repository's explicit source/version/tool record rule.

The container smoke script builds `linux/amd64`, verifies non-root user `1654`, checks missing-configuration failure, and sends SIGTERM while managed-identity token acquisition is in flight.
