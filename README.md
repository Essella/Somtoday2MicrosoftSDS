# Somtoday2MicrosoftSDS

Somtoday2MicrosoftSDS is een eenmalig uitgevoerde .NET 10-batchapplicatie. Een run leest één of meer Somtoday-instellingen, maakt daarvan één volledige Microsoft School Data Sync-dataset, uploadt deze rechtstreeks naar de tijdelijke SAS-container van SDS en wacht op de validatie-uitkomst.

Bij een HTTP-fout logt de applicatie veilig welke vaste SDS-stap mislukte en de HTTP-status. Bij een SAS-uploadfout komt ook de gevalideerde `x-ms-error-code` in de log. Response bodies, tokens, SAS-querystrings en persoonsgegevens worden niet gelogd.

De SDS-connector bepaalt automatisch het formaat: `schoolDataSyncV1` geeft V1 en `schoolDataSyncV2Rev1` geeft V2.1. Er is geen instelling voor de CSV-versie en er wordt nooit in één run zowel V1 als V2.1 geüpload.

[![Ga direct naar Uitrollen](https://img.shields.io/badge/Ga%20direct%20naar-Uitrollen-0078D4?style=for-the-badge&logo=microsoftazure&logoColor=white)](#uitrollen)

## Werking

De enige ondersteunde productievorm is een geplande Azure Container Apps Job. Iedere Job heeft:

- precies één onveranderlijke SDS-bronnaam;
- één of meer Somtoday `SchoolUUID`-waarden die samen één dataset vormen;
- eigen Somtoday-configuratie en een system-assigned managed identity.

Meerdere Jobs kunnen dezelfde Container Apps Environment gebruiken. Gebruik één extra deployment per extra SDS-bron.

De applicatie zoekt via Graph de connector met de geconfigureerde SDS-bronnaam op. De `ConnectorId` is dus geen configuratie. De bronnaam moet in SDS uniek en onveranderlijk zijn. Daarna worden succesvolle Somtoday-instellingen samengevoegd, wordt de complete gekozen CSV-set in geheugen gemaakt, en vraagt de applicatie een nieuwe uploadsession met `resetSession=true` aan. Alle bestanden gaan sequentieel naar de tijdelijke SAS-URL; daarna start en volgt de applicatie SDS-validatie.

Als één Somtoday-instelling mislukt, wordt de succesvolle subset wel als één complete dataset aangeboden, maar eindigt de run met exitcode `1`. Wanneer geen succesvolle locatie een exporteerbare klas bevat, wordt normaal niets geüpload. Header-only-modus maakt wel de volledige gekozen bestandenset.

Er is geen permanente Blob Storage, staging, rollback of Power Automate-flow. De applicatie maakt ook geen SDS-bron of connector aan.

## Eerste inrichting van SDS-connector (lege CSV-templates)

Voor de eerste inrichting van een nieuwe SDS CSV-connector uploadt de beheerder eenmalig een lege CSV-set (alleen headers) die past bij de gewenste connectorvariant.

Daarna is de connectorstructuur vastgelegd en levert deze applicatie bij volgende runs automatisch bestanden in het juiste formaat op basis van de connector (`schoolDataSyncV1` of `schoolDataSyncV2Rev1`).

Er zijn twee manieren om de lege CSV-set te verkrijgen:

- Genereren met script:
	- PowerShell: `scripts/generate-empty-csv-files.ps1`
	- Python: `scripts/generate_empty_csv_files.py`
	- Zonder parameters werken beide scripts interactief met prompts.
	- Met parameters hebben parameterwaarden voorrang op promptinvoer.
- Downloaden als release-assets:
	- Bij elke GitHub Release worden automatisch vier zipbestanden gepubliceerd:
		- `v1-no-guardians.zip`
		- `v1-with-guardians.zip`
		- `v2-no-guardians.zip`
		- `v2-with-guardians.zip`

Kies precies de set die past bij de connector die je wilt aanmaken en upload die set eenmalig in School Data Sync.

## Vereisten

- Somtoday Connect-client-ID, clientsecret en minstens één instellings-UUID.
- Een bestaande SDS CSV-bron met een unieke vaste naam en een Azure Data Lake-connector voor V1 of V2.1.
- Een Azure-abonnement en rechten om resources te maken.
- Voor het aanmaken van een syncjob: een Microsoft Entra-beheerdersrol die application roles mag toewijzen, bijvoorbeeld Global Administrator, en toestemming voor `Application.Read.All` en `AppRoleAssignment.ReadWrite.All`.
- Voor lokale ontwikkeling: .NET 10 en een `DefaultAzureCredential`-identiteit met SDS-toegang.

De Job-identiteit krijgt `IndustryData-DataConnector.Read.All` en `IndustryData-DataConnector.Upload`, plus `IndustryData.ReadBasic.All` voor het pollen van de validatieoperatie. De runtime gebruikt Microsoft Graph `/beta`; controleer wijzigingen in deze API vóór productie-upgrades.

## Uitrollen

### 1. Nieuwe Container Apps Environment

Gebruik dit voor een nieuwe Somtoday2MicrosoftSDS Container Apps Environment in een resourcegroep. Dit maakt de Container Apps Environment en de Log Analytics Workspace. De deployment slaat de naam van de Environment op in een tag van de resourcegroep.

[![Deploy new environment](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FEssella%2FSomtoday2MicrosoftSDS%2Fmain%2Finfra%2Fazuredeploy.json)

### 2. Syncjob maken

Gebruik hiervoor dezelfde resourcegroep als bij stap 1. De deployment leest de gekoppelde Environment automatisch uit de resourcegroep-tag. Je voert dus geen Environmentnaam of resource-ID opnieuw in.

[![Deploy sync job](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FEssella%2FSomtoday2MicrosoftSDS%2Fmain%2Finfra%2Fazuredeploy-sync-job.json)

### 3. Microsoft Graph-rechten toekennen

Deze stap is nodig voordat een Job SDS kan gebruiken. Het script vindt alle Somtoday2MicrosoftSDS-Jobs in zichtbare resourcegroepen met een gekoppelde Environment en vult uitsluitend ontbrekende rechten aan.

```powershell
irm "https://raw.githubusercontent.com/Essella/Somtoday2MicrosoftSDS/main/infra/assign-sync-job-roles.ps1" | iex
```

Bij de eerste uitvoering kan Microsoft Graph om toestemming voor de benodigde roltoewijzingen vragen. Je kunt deze rechten ook handmatig met Microsoft Graph-tools toekennen; zie de [deploymenthandleiding](docs/operations/DEPLOYMENT.md). De Job-template gebruikt het vaste image `ghcr.io/essella/somtoday2microsoftsds:latest`. Elke Job krijgt een deterministisch berekende UTC-minuut tussen 0 en 59, met uitvoeringen om 02:00 en 14:00 UTC.

Microsoft Entra-replicatie kan de roltoewijzing direct na het maken van de system-assigned identity tijdelijk laten mislukken. Voer in dat geval dezelfde Job-deployment nogmaals uit.

## Configuratie en export

De belangrijkste instellingen zijn `Somtoday__SchoolUUID__0` en hoger, `Somtoday__ClientId`, `Somtoday__ClientSecret`, `SchoolDataSync__SourceName`, locatie-inclusies/-exclusies, gebruikersnaamregels en optionele guardian-sync. De SDS-bronnaam moet exact overeenkomen met de vaste naam in School Data Sync. Gebruikersnaamregels ondersteunen gecompileerde IDM-templates met vaste tekst en stringbewerkingen. Gebruik voor de Somtoday-omgeving bij voorkeur de volledige leesbare naam; technisch bepaalt de eerste letter de omgeving. Header-only-uitvoer gebeurt automatisch op 31 juli (Europe/Amsterdam). Zie de [configuratiehandleiding](docs/operations/CONFIGURATION.md).

Het Somtoday-clientsecret wordt exact gebruikt zoals het is geconfigureerd; alleen een waarde die volledig uit witruimte bestaat wordt afgewezen. De applicatie volgt geen HTTP-redirects voor Somtoday-authenticatie, openbare instellingsdetectie of gegevensdownloads.

Een klas is exporteerbaar als de naam niet leeg is en na UUID-resolutie minstens één docent en één leerling overblijft. Alleen personen uit opgenomen klassen worden geëxporteerd. Docenten en leerlingen zijn matching-only; configureer SDS niet om ontbrekende accounts aan te maken. Guardians volgen aparte regels voor contacttoestemming, e-mail, naam, relatie en telefoonnormalisatie. Zie het [exportcontract](docs/contracts/EXPORT.md).

## Documentatie

| Onderwerp | Document |
|---|---|
| Bedoeld gedrag en grenzen | [Projectkern](docs/PROJECT_CORE.md) |
| CSV-inhoud en populatie | [Exportcontract](docs/contracts/EXPORT.md) |
| Graph, SAS-upload, retries en validatie | [Publicatiecontract](docs/contracts/PUBLICATION.md) |
| Configuratie | [Configuratiehandleiding](docs/operations/CONFIGURATION.md) |
| Azure deployment | [Deploymenthandleiding](docs/operations/DEPLOYMENT.md) |
| Beveiliging en privacy | [SECURITY.md](SECURITY.md) |
| Ontwikkeling | [Ontwikkelhandleiding](docs/DEVELOPMENT.md) |

## Privacy, image en licentie

De applicatie verwerkt persoonsgegevens. De school of Azure-partner blijft verantwoordelijk voor doelbinding, grondslag, verwerkersafspraken, toegangsbeheer, beveiliging, dataminimalisatie en AVG/GDPR. Log of bewaar nooit tokens, SAS-querystrings of productie-CSV's.

Het publieke image is `ghcr.io/essella/somtoday2microsoftsds:VERSIE`, ondersteunt `linux/amd64`, draait als non-root gebruiker `1654` en bevat bij releases SBOM en provenance.

Somtoday2MicrosoftSDS is beschikbaar onder [GNU AGPL v3 of later](LICENSE). Zie [NOTICE.md](NOTICE.md) en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
