# Somtoday2MicrosoftSDS

Somtoday2MicrosoftSDS is een eenmalig uitgevoerde .NET 10-batchapplicatie die actuele gegevens uit [Somtoday Connect](https://som.today/somtoday-connect/) omzet naar CSV-datasets voor [Microsoft School Data Sync](https://learn.microsoft.com/en-us/schooldatasync/data-ingestion-with-sds-v2.1-csv) (SDS) V1 en V2.1 en deze publiceert in Azure Blob Storage.

De enige ondersteunde productievorm is een geplande Azure Container Apps Job. De bedoelde beheerders zijn schoolbeheerders en Azure-partners die namens scholen werken. Lokaal uitvoeren is alleen bedoeld voor ontwikkeling en tests; er is geen ondersteunde Windows-executable.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FEssella%2FSomtoday2MicrosoftSDS%2Fmain%2Finfra%2Fazuredeploy.json)

## Begin hier

Gebruik de documentatie die bij uw taak hoort:

| Taak | Documentatie |
|---|---|
| Configuratie, locaties, gebruikersnaamregels of Key Vault | [Configuratiehandleiding](docs/operations/CONFIGURATION.md) |
| Inhoud van V1/V2.1, klassen, personen of guardians | [Exportcontract](docs/contracts/EXPORT.md) |
| Mappen, samenvoegen, staging, retries, rollback of opruimen | [Publicatiecontract](docs/contracts/PUBLICATION.md) |
| Azure-resources, identity, planning of uitrollen | [Azure-deploymenthandleiding](docs/operations/DEPLOYMENT.md) |
| Images, versies of releases | [Releasehandleiding](docs/operations/RELEASES.md) |
| Bekende verschillen tussen bedoeling en implementatie | [Afwijkingenregister](docs/DEVIATIONS.md) |
| Ontwikkelen of bijdragen | [Projectkern](docs/PROJECT_CORE.md), [ontwikkelhandleiding](docs/DEVELOPMENT.md) en [CONTRIBUTING.md](CONTRIBUTING.md) |
| Beveiliging, privacy of melden van een kwetsbaarheid | [SECURITY.md](SECURITY.md) |

De [projectkern](docs/PROJECT_CORE.md) en de gerichte contracten zijn de leidende bronnen voor bedoeld gedrag. Code en tests kunnen daarvan afwijken; iedere bekende afwijking heeft een stabiel ID in het afwijkingenregister.

## Vereisten

- Somtoday Connect-client-ID, clientsecret en minstens één instellings-UUID. Toegang is gebonden aan de voorwaarden van Somtoday; zie het [Somtoday Connect-partnerprogramma](https://som.today/somtoday-connect/contact-partnerprogramma-somtoday-connect/).
- Een Azure-abonnement en rechten om resources en roltoewijzingen te maken.
- Voor lokale ontwikkeling: de .NET 10 SDK.

De publieke productielijst met instellingen is zonder authenticatie beschikbaar via [https://api.somtoday.nl/rest/v1/connect/instelling](https://api.somtoday.nl/rest/v1/connect/instelling). Deze lijst is de gezaghebbende bron voor `Instelling.Afkorting`, ook wanneer synchronisatiegegevens uit TEST, ACCEPTATIE of NIGHTLY komen.

```powershell
Invoke-WebRequest -Uri 'https://api.somtoday.nl/rest/v1/connect/instelling'
```

Houd rekening met de door Somtoday gestelde tijdvensters en gebruiksvoorwaarden voor het synchroniseren van leerlinggegevens.

## Implementeren en gebruiken

`infra/main.bicep` maakt de benodigde Blob Storage, Key Vault, Log Analytics en geplande Container Apps Job met managed identity. Gebruik de knop hierboven of volg de [Azure-deploymenthandleiding](docs/operations/DEPLOYMENT.md). Pin productie op een releasetag of image-digest in plaats van `latest`.

De applicatie ondersteunt meerdere Somtoday-instellingen, locatie-inclusie en -exclusie, afzonderlijke gebruikersnaamregels voor docenten en leerlingen, optionele guardian-relaties en bestanden met alleen headers. Uitvoer kan onafhankelijk per instelling en per vestiging worden gegroepeerd; standaard krijgt iedere instelling één V1- en één V2.1-dataset waarin de geselecteerde vestigingen zijn samengevoegd. Voor iedere geplande scope probeert de applicatie altijd beide SDS-versies met dezelfde populatie te genereren; er bestaat geen instelling of opdrachtregeloptie om V1 of V2.1 uit te sluiten. Zie de [configuratiehandleiding](docs/operations/CONFIGURATION.md) en het [publicatiecontract](docs/contracts/PUBLICATION.md) voor alle vier indelingen en conflictregels.

Wanneer dezelfde docent of leerling bij meerdere vestigingen hoort, bevat V1 bewust één persoonsrij per vestiging met dezelfde `SIS ID` en een andere `School SIS ID`. Microsoft documenteert `SIS ID` als uniek en kan zo'n gegroepeerde V1-dataset daarom afkeuren. V2.1 neemt de persoon eenmaal op en koppelt die aan meerdere organisaties.

Tijdens een normale run neemt de applicatie een vestiging alleen op wanneer minstens één klas na UUID-resolutie zowel een docent als een leerling bevat. Bij een samengevoegde scope worden niet-geschikte vestigingen weggelaten terwijl de overige vestigingen wel worden gepubliceerd. Alleen wanneer de volledige scope geen geschikte vestiging bevat, blijft eerder gepubliceerde Blob-output ongewijzigd. De expliciete of automatische modus voor bestanden met alleen headers publiceert iedere geplande scope.

Als één instelling niet publiek kan worden gematcht, geen geldige uitvoerpaden oplevert of de brongegevens niet kunnen worden opgehaald, publiceert een samengevoegde scope de succesvol opgeloste instellingen en eindigt de run met exitcode `1`. Een conversie- of uploadfout blokkeert de betrokken SDS-versie en markeert alle deelnemende instellingen als mislukt, maar verhindert de verplichte poging voor de andere versie of een volgende scope niet. Als de publieke productielijst met instellingen zelf niet bereikbaar is, wordt geen SDS-dataset gepubliceerd.

Het publieke image is:

```text
ghcr.io/essella/somtoday2microsoftsds:VERSIE
```

Het ondersteunde image is `linux/amd64`, draait als non-root gebruiker `1654` en bevat bij releases een SBOM en build-provenance. Zie de [releasehandleiding](docs/operations/RELEASES.md) voor versiebeleid en eerste-releasebeheer.

## Privacy, beveiliging en kosten

De applicatie verwerkt persoonsgegevens van leerlingen, medewerkers en optioneel ouders of verzorgers. De school of haar Azure-partner blijft verantwoordelijk voor doelbinding, grondslag, verwerkersafspraken, toegangsbeheer, bewaartermijnen, beveiliging, dataminimalisatie en overige AVG/GDPR-verplichtingen. Guardian-sync vereist een eigen privacybeoordeling; controleer ook de gegenereerde CSV-bestanden en Microsoft SDS-instellingen vóór productiegebruik.

Sla nooit secrets of productiegegevens op in tracked configuratiebestanden. Azure-resources en toegang tot Somtoday kunnen kosten veroorzaken. Zie [SECURITY.md](SECURITY.md) en de [configuratiehandleiding](docs/operations/CONFIGURATION.md).

## Licentie en herkomst

Somtoday2MicrosoftSDS is beschikbaar onder [GNU AGPL v3 of later](LICENSE). Zie [NOTICE.md](NOTICE.md) en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Het project is afgeleid van [SomtodayOpenAPI2MicrosoftSchoolDataSync](https://github.com/Essella/SomtodayOpenAPI2MicrosoftSchoolDataSync).
