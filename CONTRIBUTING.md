# Contributing

Thank you for contributing to Somtoday2MicrosoftSDS.

## Before opening a change

Open an issue first for substantial behavioral or public-configuration changes. Keep pull requests focused and explain the operational and privacy impact. By submitting a contribution, you agree that it is licensed under `AGPL-3.0-or-later` and that you have the right to contribute it on those terms.

Do not submit credentials, access tokens, storage connection strings, personal data or production CSV files. Do not update the Somtoday OpenAPI specification or generated client without documenting its origin, version and redistribution rights.

## Build and test

Install the .NET 10 SDK and run:

```powershell
dotnet restore Somtoday2MicrosoftSDS.sln
dotnet test Somtoday2MicrosoftSDS.sln --configuration Release
dotnet publish Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj --configuration Release --runtime linux-x64 --self-contained false
```

On Windows, prefer the WSL Containers smoke-test. It builds and runs the Linux image without Docker Desktop:

```powershell
wslc version
.\scripts\Test-ContainerWsl.ps1
```

WSL Containers is currently a public preview. If `wslc.exe` is unavailable, update WSL with `wsl --update --pre-release` or use Docker as a fallback:

```powershell
docker build --tag somtoday2microsoftsds:local .
```

If Azure CLI with Bicep is installed, also run:

```powershell
az bicep build --file infra/main.bicep
```

WSLC builds for the native host architecture and has no `--platform` option. Use `-ExpectedArchitecture` with the smoke-test when you need to enforce `x86_64` or `aarch64`. Add or update tests for behavioral changes. CI must pass on Linux and Windows; the Ubuntu CI runner remains the authoritative `linux/amd64` container check with Docker. Keep logs free of secrets, authentication bodies and personal data.

## Pull requests

- Use a descriptive title and link related issues.
- Note breaking configuration changes explicitly.
- Preserve cancellation tokens for network, retry, Key Vault and Blob operations.
- Update README and third-party notices when public behavior or dependencies change.
- Do not commit generated build output.
