# Somtoday2MicrosoftSDS

Somtoday2MicrosoftSDS is een eenmalig uitgevoerde .NET 10-batchapplicatie. Een run leest één of meer Somtoday-instellingen, maakt daarvan één volledige Microsoft School Data Sync-dataset, uploadt deze rechtstreeks naar de tijdelijke SAS-container van SDS en wacht op de validatie-uitkomst.

De SDS-connector bepaalt automatisch het formaat: `schoolDataSyncV1` geeft V1 en `schoolDataSyncV2Rev1` geeft V2.1. Er is geen instelling voor de CSV-versie en er wordt nooit in één run zowel V1 als V2.1 geüpload.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FEssella%2FSomtoday2MicrosoftSDS%2Fmain%2Finfra%2Fazuredeploy.json)

## Werking

De enige ondersteunde productievorm is een geplande Azure Container Apps Job. Iedere Job heeft:

- precies één `InboundFlowId` van SDS;
- één of meer Somtoday `SchoolUUID`-waarden die samen één dataset vormen;
- eigen Somtoday-configuratie en een system-assigned managed identity.

Meerdere Jobs kunnen dezelfde Container Apps Environment gebruiken. Gebruik één extra deployment per extra inbound flow.

De applicatie zoekt via Graph eerst de connector bij de inbound flow op. De `ConnectorId` is dus geen configuratie. Daarna worden succesvolle Somtoday-instellingen samengevoegd, wordt de complete gekozen CSV-set in geheugen gemaakt, en vraagt de applicatie een nieuwe uploadsession met `resetSession=true` aan. Alle bestanden gaan sequentieel naar de tijdelijke SAS-URL; daarna start en volgt de applicatie SDS-validatie.

Als één Somtoday-instelling mislukt, wordt de succesvolle subset wel als één complete dataset aangeboden, maar eindigt de run met exitcode `1`. Wanneer geen succesvolle locatie een exporteerbare klas bevat, wordt normaal niets geüpload. Header-only-modus maakt wel de volledige gekozen bestandenset.

Er is geen permanente Blob Storage, staging, rollback of Power Automate-flow. De applicatie maakt ook geen SDS-inbound flow of connector aan.

## Vereisten

- Somtoday Connect-client-ID, clientsecret en minstens één instellings-UUID.
- Een bestaande SDS-inbound flow met een Azure Data Lake-connector voor V1 of V2.1.
- Een Azure-abonnement en rechten om resources en roltoewijzingen te maken.
- Voor deployment via Bicep: Microsoft Entra-rechten `Application.Read.All` en `AppRoleAssignment.ReadWrite.All` met de benodigde tenanttoestemming.
- Voor lokale ontwikkeling: .NET 10 en een `DefaultAzureCredential`-identiteit met SDS-toegang.

De Job-identiteit krijgt `IndustryData-InboundFlow.ReadWrite.All` en `IndustryData-DataConnector.Upload`, gelijk aan Microsofts Power Automate-route, plus `IndustryData.ReadBasic.All` voor het pollen van de validatieoperatie. De runtime gebruikt Microsoft Graph `/beta`; controleer wijzigingen in deze API vóór productie-upgrades.

## Uitrollen

`environmentMode` heeft twee keuzes:

- `new` (standaard): maakt Log Analytics en een nieuwe ACA-omgeving;
- `existing`: plaatst de Job in een bestaande ACA-omgeving via de volledige resource-ID. Die omgeving mag in een andere resourcegroep staan, maar moet in dezelfde subscription staan.

Gebruik voor de eerste Job doorgaans [main.example.bicepparam](infra/main.example.bicepparam) en voor een volgende Job [additional-job.example.bicepparam](infra/additional-job.example.bicepparam). Pin productie op een releasetag of image-digest in plaats van `latest`. ACA-cronschema's zijn UTC.

Microsoft Entra-replicatie kan de eerste roltoewijzing direct na het maken van de system-assigned identity tijdelijk laten mislukken. Voer in dat geval dezelfde deployment nogmaals uit.

## Configuratie en export

De belangrijkste instellingen zijn `Somtoday__SchoolUUID__0` en hoger, `Somtoday__ClientId`, `Somtoday__ClientSecret`, `SchoolDataSync__InboundFlowId`, locatie-inclusies/-exclusies, gebruikersnaamregels en optionele guardian-sync. Header-only-uitvoer gebeurt automatisch op 31 juli (Europe/Amsterdam). Zie de [configuratiehandleiding](docs/operations/CONFIGURATION.md).

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
