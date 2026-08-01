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

The example uses `UseDevelopmentStorage=true` for Azurite. Start Azurite or place a Development-only connection string in the ignored file. A configured service URI uses `DefaultAzureCredential` and takes precedence. The [Azurite support matrix](https://github.com/Azure/Azurite#support-matrix) records that Blob Versions are unsupported, so Azurite cannot validate the production rollback path.

## Build and test

```powershell
dotnet restore Somtoday2MicrosoftSDS.sln
dotnet test Somtoday2MicrosoftSDS.sln --configuration Release
dotnet publish Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj --configuration Release --runtime linux-x64 --self-contained false
```

### Live Somtoday OpenAPI integration tests

The normal test command does not contact Somtoday: live tests are marked with the `SomtodayIntegration` category and are skipped unless explicitly enabled. Run them against the production OpenAPI with:

```powershell
.\scripts\Test-SomtodayOpenApi.ps1 -SchoolUuid '<school-uuid>' -ClientId '<client-id>'
```

PowerShell then prompts for the mandatory `ClientSecret` as a masked secure string. Add `-IncludeGuardians` only when the credential is permitted to retrieve guardian data and that endpoint must also be covered.

The runner enables detailed console output so successful, skipped, and failed tests name every covered endpoint separately:

| Method and path | Coverage |
|---|---|
| `POST /oauth2/token?organisation={schoolUuid}` | Client-credentials authentication |
| Unauthenticated `GET https://api.somtoday.nl/rest/v1/connect/instelling` | Public production institution list and abbreviation lookup |
| `GET /rest/v1/connect/vestiging` | All permitted locations |
| `GET /rest/v1/connect/vestiging/{vestigingUuid}/lesgroep/` | Current-year groups, paginated |
| `GET /rest/v1/connect/vestiging/{vestigingUuid}/medewerker` | Current-year employees, paginated |
| `GET /rest/v1/connect/vestiging/{vestigingUuid}/leerling` | Current-year pupils, paginated |
| `GET /rest/v1/connect/vestiging/{vestigingUuid}/ouderVerzorger/` | Current-year guardians, paginated; only with `-IncludeGuardians` |

The runner passes the settings to the test process through temporary process-level environment variables, restores any previous values afterwards, and never places the secret on the `dotnet` command line. Each data endpoint is called for every permitted location. The tests validate response structure and identifiers without persisting or logging response objects, counts, credentials, or personal data. API failures are reported using only the exception type and HTTP status where available.

If Azure CLI with Bicep is installed:

```powershell
az bicep build --file infra/main.bicep
```

`infra/main.bicep` is authoritative. Do not edit `infra/azuredeploy.json` manually.

### Blob publication and rollback tests

Mandatory CI coverage uses the version-aware in-memory Blob fake in `DatasetPublisherTests`. It covers staging order, four complete promotion attempts, metadata, complete-set selection, guardian state, rollback failure, staging cleanup, cancellation, and safe logging without depending on an emulator.

Azurite is useful for basic local Blob connectivity but is not evidence for version-based recovery because Blob Versions are unsupported. An optional end-to-end check must use a temporary, non-production StorageV2 account with versioning enabled, a disposable container, the same seven-day lifecycle policy, and `Storage Blob Data Contributor` for the test identity. Use only header-only synthetic datasets, verify that server-side promotion creates versions and that a deliberately interrupted promotion can restore a complete older application-metadata group, then delete the temporary resource group. Never point this check at production output.

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
