# Somtoday2MicrosoftSDS

Somtoday2MicrosoftSDS is een .NET 10-batchapplicatie die gegevens uit [Somtoday Connect](https://som.today/somtoday-connect/) omzet naar CSV-bestanden voor [Microsoft School Data Sync](https://learn.microsoft.com/en-us/schooldatasync/data-ingestion-with-sds-v2.1-csv). Eén run verwerkt alle geconfigureerde scholen en eindigt daarna. De aanbevolen hostingvorm is daarom een geplande Azure Container Apps Job.

De applicatie maakt SDS V1- en V2.1-uitvoer per school en vestiging, ondersteunt meerdere Somtoday-scholen, locatie-inclusie en -exclusie, aangepaste gebruikersnaamformaten, optionele guardian-relaties en lege eindejaarsbestanden.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FEssella%2FSomtoday2MicrosoftSDS%2Fmain%2Finfra%2Fazuredeploy.json)

## Publicatiestatus

De broncode is bestemd voor publicatie onder `AGPL-3.0-or-later`. De releaseworkflow levert daarnaast:

- `ghcr.io/essella/somtoday2microsoftsds:<versie>` voor `linux/amd64`;
- `Somtoday2MicrosoftSDS-v<versie>-win-x64.zip` als framework-dependent Windows-build;
- een SBOM en build-provenance bij het containerimage.

Publiceer nog geen release totdat de herdistributierechten van `Somtoday2MicrosoftSDS/OpenAPIs/openapi.json` en de daaruit gegenereerde clientcode schriftelijk zijn bevestigd én de volledige Git-historie een schone secretscan heeft doorstaan. De releaseworkflow blokkeert standaard; stel pas na die schriftelijke bevestiging de repositoryvariabele `OPENAPI_REDISTRIBUTION_CONFIRMED=true` in. Het eerdere Iconfinder-icoon en alle iconreferenties zijn verwijderd.

Na de eerste release moet een repositorybeheerder het GHCR-package eenmalig via [GitHub Packages](https://docs.github.com/en/packages/learn-github-packages/introduction-to-github-packages) op **Public** zetten. Hernoem na merge ook de GitHub-repository naar `Somtoday2MicrosoftSDS` en pas branch rulesets, vereiste CI, CodeQL, secret scanning en push protection toe.

## Vereisten

- Somtoday Connect-client-ID, clientsecret en één of meer organisatie-UUID's. Toegang is aan voorwaarden van Somtoday gebonden; zie het [Somtoday Connect-partnerprogramma](https://som.today/somtoday-connect/contact-partnerprogramma-somtoday-connect/). Een organisatie-UUID kan met passende toegang via `https://api.somtoday.nl/rest/v1/connect/instelling` worden opgezocht.
- Voor Azure: een abonnement en rechten om resourceproviders, rollen en resources te beheren.
- Voor lokaal bouwen: .NET 10 SDK. Op Windows kan de container lokaal zonder Docker Desktop worden gebouwd en getest met [WSL Containers](https://learn.microsoft.com/en-us/windows/wsl/wsl-container) (`wslc.exe`); Docker blijft een optionele fallback.

Houd rekening met de door Somtoday gestelde tijdvensters en gebruiksvoorwaarden voor het synchroniseren van leerlinggegevens.

## Configuratie

.NET-configuratie wordt geladen uit `appsettings.json`, het omgevingsbestand, [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0) in Development en environment variables. Environment variables hebben voorrang. Gebruik `__` als platformonafhankelijke scheiding, bijvoorbeeld `Somtoday__ClientId`.

| Instelling | Production | Development |
|---|---|---|
| `DOTNET_ENVIRONMENT` | `Production` | Moet expliciet `Development` zijn voor lokale fallbacks |
| `KeyVault__VaultUri` | Verplicht; HTTPS | Optioneel |
| `KeyVault__SomtodayClientSecretName` | Optioneel, standaard `somtoday-client-secret` | Zelfde |
| `Somtoday__ClientSecret` | Alleen tijdelijke bootstrap/rotatie voor Key Vault | Direct secret wanneer `VaultUri` leeg is |
| `Somtoday__ClientId` | Verplicht | Verplicht |
| `Somtoday__SchoolUUID__0` en hoger | Minstens één unieke UUID | Minstens één unieke UUID |
| `Somtoday__Environment` | `PROD`, `TEST`, `ACCEPTATIE` of `NIGHTLY`; standaard `PROD` | Zelfde |
| `Storage__AzureBlob__ServiceUri` | Verplicht | Heeft voorrang wanneer ingevuld |
| `Storage__AzureBlob__ConnectionString` | Verboden | Alleen toegestaan wanneer `ServiceUri` leeg is |
| `Storage__AzureBlob__Container` | Verplicht; standaard `sds` | Zelfde |
| `Output__Folder` | Standaard `sds/output` | Zelfde |
| `Output__GenerateEmptyCsv` | Standaard `false` | Zelfde |
| `Locations__IncludedLocationCodes__0` en hoger | Optioneel; leeg is alle locaties | Zelfde |
| `Locations__ExcludedLocationCodes__0` en hoger | Optioneel; exclusie heeft voorrang | Zelfde |
| `UsernameFormat__Teacher` / `__Student` | Standaard `Emailadres` | Zelfde |
| `SchoolDataSync__EnableGuardianSync` | Standaard `false` | Zelfde |

`appsettings.json` bevat alleen lege of niet-gevoelige defaults. Zet nooit een werkelijk secret of productie-connectionstring in een tracked configuratiebestand.

### Key Vault-bootstrap en rotatie

Wanneer `KeyVault__VaultUri` is ingevuld, is Key Vault altijd de bron van het effectieve Somtoday-secret:

1. Ontbreekt het Vault-secret en is `Somtoday__ClientSecret` aanwezig, dan wordt de eerste versie opgeslagen.
2. Wijkt de tijdelijke waarde af, dan wordt precies één nieuwe Vault-versie opgeslagen en verschijnt `Secret in Vault bijgewerkt.`
3. Komt de waarde overeen, dan wordt niets gewijzigd en verschijnt `Secret komt overeen met Vault. Verwijder Somtoday__ClientSecret uit de environment-configuratie.`
4. Zonder tijdelijke waarde wordt de actuele Vault-versie gebruikt.

Een ontbrekend Vault-secret zonder bootstrapwaarde, een mislukte update of iedere andere Vault-fout beëindigt de run met exitcode `1`. Secretwaarden en OAuth-responsebody's worden niet gelogd. Oude secretversies blijven in Key Vault beschikbaar voor audit en herstel.

Na een succesvolle bootstrap of rotatie moet je opnieuw deployen zonder `somtodayClientSecret`. Daarmee verdwijnen zowel het Container Apps-secret als de environmentverwijzing.

### Uitvoer

De paden hebben deze vorm:

```text
sds/output/{SchoolAfkorting}/{VestigingsAfkorting}/v1/
sds/output/{SchoolAfkorting}/{VestigingsAfkorting}/v2/
```

Slashes in afkortingen worden `_`. Conflicterende paden laten de betrokken school falen. Later uitgesloten vestigingen worden niet automatisch verwijderd.

Met `Output__GenerateEmptyCsv=true`, argument `--empty-csv`, of automatisch op 31 juli worden alleen CSV-headers geschreven. Exitcode `0` betekent dat alle scholen volledig zijn verwerkt; exitcode `1` betekent configuratie-, authenticatie-, opslag-, cancellation- of schoolfouten.

Guardian-sync kan persoonsgegevens van ouders/verzorgers toevoegen. Somtoday geeft niet door of een leerling ouder dan 18 toestemming voor ouderinzage heeft gegeven. Laat de wekelijkse guardian-samenvatting in Microsoft 365 daarom uitgeschakeld en voer vóór gebruik een eigen privacybeoordeling uit.

## Azure quickstart met Bicep

De template in `infra/main.bicep` maakt een Storage-account en private Blobcontainer, een Key Vault met RBAC/soft delete/purge protection, Log Analytics, een Consumption Container Apps-omgeving en een [scheduled Container Apps Job](https://learn.microsoft.com/en-us/azure/container-apps/jobs). De system-assigned managed identity krijgt `Storage Blob Data Contributor` alleen op de uitvoercontainer en [`Key Vault Secrets Officer`](https://learn.microsoft.com/en-us/azure/key-vault/general/rbac-guide) alleen op de app-specifieke Vault.

Standaarden: publieke `latest`-image, `0 1 * * *` UTC, één replica, `0.5` vCPU, `1Gi` geheugen, timeout 3600 seconden en één retry. Pin voor productie bij voorkeur `imageReference` op een released tag of digest.

### Deploy via de Azure Portal

De knop gebruikt de gegenereerde ARM-template `infra/azuredeploy.json`, omdat een externe Azure Portal-deployment geen Bicep-bestand rechtstreeks kan laden. `infra/main.bicep` blijft de bron; wijzig `azuredeploy.json` niet handmatig. CI controleert dat beide templates inhoudelijk gelijk blijven.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FEssella%2FSomtoday2MicrosoftSDS%2Fmain%2Finfra%2Fazuredeploy.json)

Voor deze route moet de repository openbaar zijn en moet het gekozen GHCR-image publiek bestaan. Je hebt daarnaast Azure-rechten nodig om de resources én de twee roltoewijzingen te maken, bijvoorbeeld `Owner`, of `Contributor` gecombineerd met `User Access Administrator`, op de resourcegroep.

1. Selecteer **Deploy to Azure**, meld je aan en kies een abonnement, resourcegroep en regio.
2. Vul minimaal `schoolUuids` als JSON-array en `somtodayClientId` in. Pin `imageReference` bij voorkeur op een releasetag.
3. Vul `somtodayClientSecret` alleen bij de eerste bootstrap of een rotatie in. Dit is een secure ARM-parameter.
4. Controleer de voorwaarden en start de deployment.
5. Start na RBAC-propagatie één handmatige jobrun en controleer de logs en Blob-uitvoer.
6. Open daarna dezelfde deploymentknop opnieuw, gebruik dezelfde resourcegroep en `namePrefix`, laat `somtodayClientSecret` leeg en deploy opnieuw. Daarmee verdwijnen het tijdelijke Container Apps-secret en de environmentverwijzing; de applicatie gebruikt voortaan Key Vault.

De portaldeployment maakt geen gratis Azure-abonnement of Somtoday-toegang aan. De gemaakte resources kunnen kosten veroorzaken.

### Deploy via Azure CLI

Maak een lokale parameterfile die door Git wordt genegeerd:

```powershell
Copy-Item infra/main.example.bicepparam infra/main.bicepparam
# Vul schoolUuids en somtodayClientId in.

az group create --name rg-somtoday-sds --location westeurope
az deployment group create `
  --resource-group rg-somtoday-sds `
  --parameters infra/main.bicepparam
```

Voor de eerste bootstrap of een rotatie bied je de secure parameter tijdelijk via de huidige shell aan:

```powershell
$env:SOMTODAY_BOOTSTRAP_SECRET = Read-Host 'Somtoday clientsecret' -MaskInput
az deployment group create `
  --resource-group rg-somtoday-sds `
  --parameters infra/main.bicepparam
```

`main.bicepparam` leest `SOMTODAY_BOOTSTRAP_SECRET` alleen tijdens de Bicep-compilatie via `readEnvironmentVariable`; de waarde staat niet als plaintext in het bestand of als uitgeschreven deploymentargument. De `@secure()`-parameter voorkomt weergave in de Azure-deploymenthistorie.

Start een handmatige acceptatierun. Bij een net aangemaakte identity kan RBAC-propagatie enkele minuten duren:

```powershell
az containerapp job start --name somtodaysds-job --resource-group rg-somtoday-sds
az containerapp job execution list --name somtodaysds-job --resource-group rg-somtoday-sds --output table
az containerapp job logs show --name somtodaysds-job --resource-group rg-somtoday-sds --container somtoday2microsoftsds --follow --tail 100
```

Verwijder daarna de tijdelijke waarde en deploy opnieuw. De tweede deployment bevat geen jobsecret:

```powershell
Remove-Item Env:SOMTODAY_BOOTSTRAP_SECRET
az deployment group create `
  --resource-group rg-somtoday-sds `
  --parameters infra/main.bicepparam
```

De cronplanning van Container Apps Jobs wordt in UTC geëvalueerd, ook bij zomer- en wintertijd.

## Lokaal ontwikkelen

Kopieer het voorbeeldbestand; de doelnaam is genegeerd:

```powershell
Copy-Item Somtoday2MicrosoftSDS/appsettings.Development.example.json Somtoday2MicrosoftSDS/appsettings.Development.json
dotnet user-secrets set --project Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj 'Somtoday:ClientSecret' '<secret>'
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj
```

Het voorbeeld gebruikt `UseDevelopmentStorage=true` voor Azurite. Start Azurite lokaal of vervang dit in het genegeerde bestand door een Development-connectionstring. Een `ServiceUri` gebruikt `DefaultAzureCredential` en heeft altijd voorrang.

Zelf bouwen en testen:

```powershell
dotnet restore Somtoday2MicrosoftSDS.sln
dotnet test Somtoday2MicrosoftSDS.sln --configuration Release
dotnet publish Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj --configuration Release --runtime win-x64 --self-contained false
```

### Lokale containertest met WSL Containers

WSL Containers (`wslc.exe`) kan het Linux-image rechtstreeks via WSL bouwen en uitvoeren, zonder Docker Desktop. De functie is momenteel een public preview en vereist een actuele WSL-pre-release. Controleer eerst of de CLI beschikbaar is:

```powershell
wslc version
```

Ontbreekt `wslc.exe`, werk WSL dan bij met `wsl --update --pre-release`, open een nieuwe PowerShell-sessie en controleer de versie opnieuw. Voer daarna de lokale build-, non-root-, label-, ontbrekende-configuratie- en SIGTERM/cancellation-smoketests uit met:

```powershell
.\scripts\Test-ContainerWsl.ps1
```

WSLC bouwt voor de native hostarchitectuur en heeft geen `--platform`-optie. Het script accepteert daarom standaard `x86_64` en `aarch64`; gebruik zo nodig `-ExpectedArchitecture`. De GitHub Actions-runner gebruikt Docker op Ubuntu en blijft de gezaghebbende `linux/amd64`-controle voor het release-image.

## Publiek image gebruiken

```powershell
docker pull ghcr.io/essella/somtoday2microsoftsds:latest
```

Docker-gebruikers kunnen voor een lokale Development-run configuratie als host-environmentvariabelen doorgeven, zonder secretwaarden in de commandoregel te zetten:

```powershell
$env:Somtoday__ClientId = '<client-id>'
$env:Somtoday__ClientSecret = '<client-secret>'
$env:Somtoday__SchoolUUID__0 = '<school-uuid>'
$env:Storage__AzureBlob__ConnectionString = '<azurite-connectionstring>'

docker run --rm `
  --env DOTNET_ENVIRONMENT=Development `
  --env Somtoday__ClientId `
  --env Somtoday__ClientSecret `
  --env Somtoday__SchoolUUID__0 `
  --env Storage__AzureBlob__ConnectionString `
  ghcr.io/essella/somtoday2microsoftsds:latest
```

Het image draait als non-root gebruiker `1654`. In Azure worden geen registrycredentials gebruikt nadat het GHCR-package Public is gemaakt.

## Releases en versiebeleid

Publiceer een GitHub Release met een vierdelige tag zoals `v1.2.3.4`. Elk onderdeel moet tussen `0` en `65534` liggen. De workflow test Linux en Windows en gebruikt `1.2.3.4` voor `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`, de Windows-ZIP en de container-tag. Daarnaast verschijnen `sha-<commit>` en `latest`.

Lokale builds hebben standaard versie `0.0.0.0`. Actions zijn op volledige commit-SHA's vastgezet en Dependabot bewaakt NuGet, Docker en GitHub Actions.

## Kosten, privacy en verantwoordelijkheid

Broncode en het publieke GHCR-image kunnen kosteloos worden verkregen. Somtoday-toegang en Azure-resources zijn niet gratis gegarandeerd. [Container Apps Consumption](https://learn.microsoft.com/en-us/azure/container-apps/billing) kent gratis maandelijkse hoeveelheden, maar Container Apps-verbruik boven de grens, Key Vault, Blob Storage en Log Analytics kunnen kosten veroorzaken.

De applicatie verwerkt gegevens van leerlingen, medewerkers en mogelijk ouders/verzorgers. De gebruiker of deployende organisatie is verantwoordelijk voor doelbinding, grondslag, verwerkersafspraken, toegangsbeheer, bewaartermijnen, beveiliging, dataminimalisatie en overige AVG/GDPR-verplichtingen. Controleer de gegenereerde CSV's en Microsoft SDS-instellingen vóór productiegebruik.

## Licentie en bijdragen

Somtoday2MicrosoftSDS is beschikbaar onder [GNU AGPL v3 of later](LICENSE). Zie [NOTICE.md](NOTICE.md), [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), [SECURITY.md](SECURITY.md) en [CONTRIBUTING.md](CONTRIBUTING.md).

Dit project is afgeleid van [SomtodayOpenAPI2MicrosoftSchoolDataSync](https://github.com/Essella/SomtodayOpenAPI2MicrosoftSchoolDataSync).
