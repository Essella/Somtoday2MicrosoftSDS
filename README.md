# Somtoday2MicrosoftSDS

Somtoday2MicrosoftSDS is een .NET 10-batchapplicatie die gegevens uit [Somtoday Connect](https://som.today/somtoday-connect/) omzet naar CSV-bestanden voor [Microsoft School Data Sync](https://learn.microsoft.com/en-us/schooldatasync/data-ingestion-with-sds-v2.1-csv). Eén run verwerkt alle geconfigureerde Somtoday-instellingen en eindigt daarna.

De enige ondersteunde productievorm is een geplande Azure Container Apps Job. De bedoelde beheerders zijn schoolbeheerders en Azure-partners die namens scholen werken. Lokaal uitvoeren wordt ondersteund voor ontwikkeling en tests; er is geen ondersteunde Windows-executable of andere productiehostingvorm.

De applicatie maakt altijd SDS V1- en V2.1-uitvoer. Ze ondersteunt meerdere Somtoday-instellingen, locatie-inclusie en -exclusie, instelbare uitvoerindeling, aangepaste gebruikersnaamformaten, optionele guardian-relaties en lege eindejaarsbestanden.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FEssella%2FSomtoday2MicrosoftSDS%2Fmain%2Finfra%2Fazuredeploy.json)

## Projectdocumentatie

[docs/PROJECT_CONTEXT.md](docs/PROJECT_CONTEXT.md) is de leidende bron voor bevestigde scope, verantwoordelijkheden, invarianten en afwijkingen in de huidige implementatie. Bijdragers en geautomatiseerde hulpmiddelen moeten ook [AGENTS.md](AGENTS.md) volgen. Deze README blijft bewust Nederlandstalig voor beheerders; overige project- en bijdragersdocumentatie is Engelstalig.

## Publicatiestatus

De broncode is bestemd voor publicatie onder AGPL-3.0-or-later. Het ondersteunde releaseartefact is:

- ghcr.io/essella/somtoday2microsoftsds:VERSIE voor linux/amd64, inclusief SBOM en build-provenance.

De projectowner heeft de clientcode zelf met Visual Studio gegenereerd en bevestigt dat geen aanvullende herdistributiebevestiging nodig is. De repository is nieuw en heeft nooit een secret bevat; een volledige historische secretscan is daarom geen releasevoorwaarde.

De huidige releaseworkflow bevat nog een overbodige herdistributiepoort en publiceert nog een Windows-archief. Beide botsen met de bevestigde projectstatus en staan in de projectcontext als implementatieafwijkingen.

Na de eerste release moet een repositorybeheerder het GHCR-package via [GitHub Packages](https://docs.github.com/en/packages/learn-github-packages/introduction-to-github-packages) eenmalig op **Public** zetten. Hernoem na merge ook de GitHub-repository naar Somtoday2MicrosoftSDS en stel branch rulesets, vereiste CI, CodeQL, secret scanning en push protection in.

## Vereisten

- Een Somtoday Connect-client-ID, clientsecret en één of meer instellings-UUID's. Toegang is aan de voorwaarden van Somtoday gebonden; zie het [Somtoday Connect-partnerprogramma](https://som.today/somtoday-connect/contact-partnerprogramma-somtoday-connect/).
- Een Azure-abonnement en rechten om resourceproviders, rollen en resources te beheren.
- De .NET 10 SDK voor lokale ontwikkeling.

De publieke productielijst met Somtoday-instellingen bevat UUID, naam, afkorting en BRIN-identificaties en is zonder authenticatie beschikbaar via [https://api.somtoday.nl/rest/v1/connect/instelling](https://api.somtoday.nl/rest/v1/connect/instelling). Zowel beheerders als de applicatie gebruiken deze lijst als gezaghebbende bron voor Instelling.Afkorting, ook wanneer de synchronisatiegegevens uit TEST, ACCEPTATIE of NIGHTLY komen.

De lijst is ook vanuit PowerShell te bekijken:

~~~powershell
Invoke-WebRequest -Uri 'https://api.somtoday.nl/rest/v1/connect/instelling'
~~~

Houd rekening met de door Somtoday gestelde tijdvensters en gebruiksvoorwaarden voor het synchroniseren van leerlinggegevens.

## Configuratie

.NET-configuratie wordt geladen uit appsettings.json, het omgevingsbestand, [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0) in Development en environment variables. Environment variables hebben voorrang. Gebruik een dubbele underscore als platformonafhankelijke scheiding, bijvoorbeeld Somtoday__ClientId.

| Instelling | Production | Development |
|---|---|---|
| DOTNET_ENVIRONMENT | Production | Moet expliciet Development zijn voor lokale fallbacks |
| KeyVault__VaultUri | Verplichte HTTPS-URI | Optioneel |
| KeyVault__SomtodayClientSecretName | Optioneel; standaard somtoday-client-secret | Zelfde |
| Somtoday__ClientSecret | Alleen tijdelijke Key Vault-bootstrap of -rotatie | Direct secret wanneer VaultUri leeg is |
| Somtoday__ClientId | Verplicht | Verplicht |
| Somtoday__SchoolUUID__0 en hoger | Minstens één unieke UUID | Zelfde |
| Somtoday__Environment | Volledige naam PROD, TEST, ACCEPTATIE of NIGHTLY heeft de voorkeur; de eerste letter bepaalt de omgeving | Zelfde |
| Storage__AzureBlob__ServiceUri | Verplicht | Heeft voorrang wanneer ingevuld |
| Storage__AzureBlob__ConnectionString | Verboden | Alleen toegestaan wanneer ServiceUri leeg is |
| Storage__AzureBlob__Container | Verplicht; standaard sds | Zelfde |
| Output__Folder | Standaard sds/output | Zelfde |
| Output__GenerateEmptyCsv | Standaard false | Zelfde |
| Output__SeparateByInstitution | Standaard true | Zelfde |
| Output__SeparateByLocation | Standaard false | Zelfde |
| Locations__IncludedLocationCodes__0 en hoger | Optioneel; leeg betekent alle locaties | Zelfde |
| Locations__ExcludedLocationCodes__0 en hoger | Optioneel; exclusie heeft voorrang | Zelfde |
| UsernameFormat__Teacher en __Student | Standaard Emailadres | Zelfde |
| SchoolDataSync__EnableGuardianSync | Standaard false | Zelfde |

De twee instellingen voor de uitvoerindeling zijn bevestigd bedoeld gedrag, maar bestaan nog niet in de huidige implementatie. Zie de projectcontext voordat je de applicatie gebruikt of wijzigt.

Volledige omgevingsnamen hebben de voorkeur omdat ze voor beheerders het duidelijkst zijn. De parser gebruikt bewust alleen de eerste niet-lege letter. Dit voorkomt configuratiefouten door kleine typefouten zolang de beginletters van de vier omgevingen uniek blijven.

Tracked configuratiebestanden bevatten uitsluitend lege of niet-gevoelige standaardwaarden. Zet nooit een werkelijk secret of een productie-connectionstring in een tracked bestand.

### Gebruikersnaamregels

`UsernameFormat__Teacher` en `UsernameFormat__Student` zijn bewust instelbare expressieregels. Een schoolbeheerder kan daarmee de gebruikersnaamlogica van de eigen IDM-oplossing beschrijven. Dit is bijvoorbeeld nodig wanneer die IDM-oplossing een ander e-mailadres genereert dan het adres dat in Somtoday is opgeslagen. De twee instellingen worden afzonderlijk toegepast op medewerkers en leerlingen.

Een losse propertynaam wordt automatisch als user-property behandeld. De huidige expressiesyntaxis ondersteunt daarnaast meerdere properties, vaste tekst en Dynamic LINQ-bewerkingen. Voor een gebruiker met `Voorletters = J`, `Achternaam = Jansen`, `Gebruikersnaam = jjansen` en `Emailadres = J.Jansen@School.nl` zijn dit voorbeelden:

| Configuratiewaarde | Uitvoer |
|---|---|
| `Emailadres` | `J.Jansen@School.nl` |
| `Gebruikersnaam` | `jjansen` |
| `{user.Voorletters}.{user.Achternaam}` | `J.Jansen` |
| `{user.Emailadres.ToLower()}` | `j.jansen@school.nl` |
| `{user.Voorletters + "." + user.Achternaam + "@school.nl"}` | `J.Jansen@school.nl` |

Gebruik alleen properties die voor het bedoelde accounttype beschikbaar en geschikt zijn. De gegenereerde Somtoday-modellen bevatten ook persoonsgegevens die nooit als gebruikersnaam horen te worden gebruikt. Test beide regels met representatieve medewerkers en leerlingen voordat je ze in productie toepast.

### Key Vault-bootstrap en rotatie

Wanneer KeyVault__VaultUri is ingevuld, is Key Vault altijd de bron van het effectieve Somtoday-secret:

1. Ontbreekt het Vault-secret en is Somtoday__ClientSecret aanwezig, dan slaat de applicatie de eerste versie op.
2. Wijkt de tijdelijke waarde af van de opgeslagen waarde, dan wordt precies één nieuwe Vault-versie opgeslagen.
3. Komen beide waarden overeen, dan vindt geen write plaats en vraagt de applicatie de tijdelijke environmentconfiguratie te verwijderen.
4. Zonder tijdelijke waarde gebruikt de applicatie de actuele Vault-versie.

Een ontbrekend Vault-secret zonder bootstrapwaarde, een mislukte update of iedere andere Vault-fout beëindigt de run met exitcode 1. Secretwaarden en OAuth-responsebody's worden niet gelogd. Oude secretversies blijven voor audit en herstel beschikbaar.

Deploy na een succesvolle bootstrap of rotatie opnieuw zonder somtodayClientSecret. Daarmee verdwijnen het tijdelijke Container Apps-secret en de bijbehorende environmentverwijzing.

## Uitvoercontract

Een **Somtoday-instelling** is één Somtoday-instance die door een geconfigureerde instellings-UUID en een Instelling-record wordt geïdentificeerd.

Twee booleans bepalen of de uitvoer per Somtoday-instelling en per locatie wordt gescheiden:

| Per instelling | Per locatie | Pad onder de geconfigureerde uitvoerprefix | Bereik van de dataset |
|---|---|---|---|
| false | false | v1/ en v2/ | Alle instellingen en geselecteerde locaties worden per SDS-versie samengevoegd. |
| true | false | {InstellingAfkorting}/v1/ en v2/ | Eén dataset per Somtoday-instelling; geselecteerde locaties worden samengevoegd. Dit is de standaard. |
| false | true | {VestigingAfkorting}/v1/ en v2/ | Eén dataset per vestigingsafkorting zonder instellingsmap. |
| true | true | {InstellingAfkorting}/{VestigingAfkorting}/v1/ en v2/ | Eén dataset per geselecteerde vestiging onder haar Somtoday-instelling. |

Wanneer alleen per locatie wordt gescheiden en twee instellingen dezelfde gesaniteerde vestigingsafkorting hebben, faalt de run niet. Alleen de botsende mapnamen krijgen de vorm INSTELLING_VESTIGING, met de bijbehorende gesaniteerde Instelling.Afkorting als prefix.

Slashes en backslashes in afkortingen worden underscores. Andere conflicterende paden laten de betrokken instelling falen.

De bedoelde transformatie- en publicatieregels zijn:

- Iedere geselecteerde locatie levert geldige SDS-bestanden met alle exporteerbare gegevens die beschikbaar zijn, ook wanneer een Somtoday-dataset leeg is.
- V1 en V2.1 gebruiken dezelfde populatieregels en worden beide bij iedere normale run gemaakt.
- Een klas wordt alleen opgenomen wanneer na filtering minstens één opgeloste, exporteerbare docent én één opgeloste, exporteerbare leerling overblijven.
- Klassen met alleen docenten, klassen met alleen leerlingen en personen zonder opgenomen klas worden uit alle bestanden weggelaten.
- Iedere resulterende dataset per SDS-versie is een zelfstandige publicatie-eenheid.
- Alle bestanden van zo'n dataset worden eerst volledig in een stagingmap opgeslagen. Pas daarna worden ze naar de werkelijke bestemming gepromoveerd.
- Vóór promotie maakt de applicatie Blob-snapshots van alle bestaande doelbestanden en registreert ze welke doelbestanden nog niet bestaan.
- Mislukt staging of promotie, dan krijgt de dataset in totaal drie pogingen. Iedere poging heeft een timeout van twee minuten.
- Tijdens een geslaagde promotie mag tijdelijk een combinatie van oude en nieuwe bestanden zichtbaar zijn.
- Mislukt staging na drie pogingen, dan blijft de bestaande doeluitvoer onaangeroerd. Mislukt promotie na drie pogingen, dan herstelt de applicatie de snapshots en verwijdert ze bestanden die vóór promotie nog niet bestonden. De rollback moet volledig zijn afgerond voordat de applicatie doorgaat; gedeeltelijke doeluitvoer is niet toegestaan. Een succesmarker is niet nodig. Verhindert een Blob-storing ook de rollback, dan stopt de volledige applicatie en worden geen volgende datasets verwerkt. Een volgende run hervat geen rollback of stagingdata, maar downloadt opnieuw actuele Somtoday-data en maakt de volledige dataset opnieuw.
- Wanneer guardian-sync is uitgeschakeld, worden eerder gemaakte V1 User.csv en Guardianrelationship.csv en V2 relationships.csv uit de betrokken uitvoerpaden verwijderd.
- Wanneer guardian-sync is ingeschakeld maar geen guardians of relaties oplevert, worden de guardianbestanden met alleen headers gepubliceerd.
- Automatische opschoning blijft beperkt tot guardianbestanden. Uitvoer voor hernoemde, verwijderde of later uitgesloten instellingen en locaties blijft staan.

De huidige implementatie voldoet nog niet aan al deze regels. Raadpleeg de implementatieafwijkingen in [docs/PROJECT_CONTEXT.md](docs/PROJECT_CONTEXT.md) voordat je de applicatie gebruikt of wijzigt.

Output__GenerateEmptyCsv=true, argument --empty-csv of de automatische trigger op 31 juli maakt bestanden met alleen headers. Schooljaargrenzen en de 31-julitrigger gebruiken lokale Europe/Amsterdam-tijd, inclusief CET of CEST. Exitcode 0 staat voor een volledig geslaagde run. Configuratie-, authenticatie-, opslag-, cancellation- of instellingsfouten geven exitcode 1.

### Guardians

Guardian-sync kan persoonsgegevens van ouders en verzorgers toevoegen. Somtoday geeft niet door of een leerling ouder dan 18 toestemming voor ouderinzage heeft gegeven. Laat de wekelijkse guardian-samenvatting in Microsoft 365 daarom uitgeschakeld en voer vóór gebruik een eigen privacybeoordeling uit.

Microsoft SDS gebruikt aparte naamvelden:

- SDS V1 user.csv: Email, First Name, Last Name, optioneel Phone en optioneel SIS ID.
- SDS V2.1 users.csv: sourcedId, username, givenName, familyName, email en optioneel phone voor guardians die vanuit relationships.csv worden gekoppeld.

De bevestigde Somtoday-mapping is:

- First Name en givenName = Voorletters.
- Last Name en familyName = Voorvoegsel en Achternaam, met één spatie tussen niet-lege delen.
- Email = Emailadres.
- Phone = het naar E.164 genormaliseerde Telefoonnummer.

Wanneer WenstContactViaEMail false is, worden de volledige guardian en al haar relaties niet geëxporteerd. De bron van Telefoonnummer wordt bepaald door de fallbackwaarde te vergelijken met de expliciete thuis-, mobiel- en werknummers. Een nummer met de bijbehorende geheimnummer-vlag wordt niet geëxporteerd. Wanneer meerdere niet-geheime nummers beschikbaar zijn, geldt de voorkeur mobiel, daarna thuis en daarna werk.

Zie de officiële [SDS V1-specificatie](https://learn.microsoft.com/en-us/schooldatasync/sds-v1-csv-file-format), [SDS V2.1-specificatie](https://learn.microsoft.com/en-us/schooldatasync/sds-v2.1-csv-file-format) en [guardian-uitleg](https://learn.microsoft.com/en-us/schooldatasync/parents-and-guardians-in-sds).

## Azure quickstart met Bicep

infra/main.bicep maakt een Storage-account en private Blobcontainer, een Key Vault met RBAC, soft delete en purge protection, Log Analytics, een Consumption Container Apps-omgeving en een geplande Azure Container Apps Job.

De system-assigned managed identity krijgt Storage Blob Data Contributor alleen op de uitvoercontainer en Key Vault Secrets Officer alleen op de applicatiespecifieke Vault.

Standaarden zijn een publiek latest-image, planning 0 1 * * * UTC, één replica, 0.5 vCPU, 1 GiB geheugen, een timeout van 3.600 seconden en één retry. Pin imageReference in productie op een releasetag of digest.

### Deploy via de Azure Portal

De deploymentknop gebruikt infra/azuredeploy.json omdat een externe Portaldeployment geen Bicep-bestand rechtstreeks kan laden. infra/main.bicep blijft de bron. Wijzig azuredeploy.json niet handmatig; CI controleert dat beide gelijk blijven.

1. Selecteer **Deploy to Azure**, meld je aan en kies abonnement, resourcegroep en regio.
2. Vul minimaal schoolUuids als JSON-array en somtodayClientId in. Gebruik bij voorkeur een releasetag.
3. Vul somtodayClientSecret alleen in voor de eerste bootstrap of een rotatie.
4. Start na RBAC-propagatie één handmatige jobrun en controleer logs en Blob-uitvoer.
5. Deploy daarna opnieuw met dezelfde resourcegroep en namePrefix, maar zonder somtodayClientSecret.

De deployment maakt geen gratis Azure-abonnement of Somtoday-toegang aan. Azure-resources kunnen kosten veroorzaken.

### Deploy via Azure CLI

Maak een lokale parameterfile die Git negeert:

~~~powershell
Copy-Item infra/main.example.bicepparam infra/main.bicepparam
# Vul schoolUuids en somtodayClientId in.

az group create --name rg-somtoday-sds --location westeurope
az deployment group create --resource-group rg-somtoday-sds --parameters infra/main.bicepparam
~~~

Bied een eerste bootstrap- of rotatiewaarde tijdelijk via de huidige shell aan:

~~~powershell
$env:SOMTODAY_BOOTSTRAP_SECRET = Read-Host 'Somtoday clientsecret' -MaskInput
az deployment group create --resource-group rg-somtoday-sds --parameters infra/main.bicepparam
~~~

Start na RBAC-propagatie een handmatige acceptatierun:

~~~powershell
az containerapp job start --name somtodaysds-job --resource-group rg-somtoday-sds
az containerapp job execution list --name somtodaysds-job --resource-group rg-somtoday-sds --output table
az containerapp job logs show --name somtodaysds-job --resource-group rg-somtoday-sds --container somtoday2microsoftsds --follow --tail 100
~~~

Verwijder daarna de tijdelijke waarde en deploy opnieuw:

~~~powershell
Remove-Item Env:SOMTODAY_BOOTSTRAP_SECRET
az deployment group create --resource-group rg-somtoday-sds --parameters infra/main.bicepparam
~~~

Container Apps Job-cronplanningen worden in UTC geëvalueerd.

## Lokaal ontwikkelen

Kopieer het genegeerde Development-voorbeeld en bewaar het secret met .NET User Secrets:

~~~powershell
Copy-Item Somtoday2MicrosoftSDS/appsettings.Development.example.json Somtoday2MicrosoftSDS/appsettings.Development.json
dotnet user-secrets set --project Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj 'Somtoday:ClientSecret' '<secret>'
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj
~~~

Het voorbeeld gebruikt UseDevelopmentStorage=true voor Azurite. Start Azurite lokaal of gebruik in het genegeerde bestand een Development-connectionstring. Een ServiceUri gebruikt DefaultAzureCredential en heeft altijd voorrang.

Bouwen en testen:

~~~powershell
dotnet restore Somtoday2MicrosoftSDS.sln
dotnet test Somtoday2MicrosoftSDS.sln --configuration Release
~~~

### Lokale containertest op Windows

[WSL Containers](https://learn.microsoft.com/en-us/windows/wsl/wsl-container) kan het Linux-image zonder Docker Desktop bouwen en uitvoeren. Dit is lokale ontwikkeltooling en geen ondersteunde Windows-deployment.

~~~powershell
wslc version
./scripts/Test-ContainerWsl.ps1
~~~

WSL Containers is momenteel een public preview en bouwt voor de native hostarchitectuur. De GitHub Actions Ubuntu-runner blijft de gezaghebbende linux/amd64-imagecontrole.

## Publiek image

~~~powershell
docker pull ghcr.io/essella/somtoday2microsoftsds:latest
~~~

Het image draait als non-root gebruiker 1654. Azure heeft geen registrycredentials nodig nadat het GHCR-package publiek is gemaakt.

## Releases en versiebeleid

Publiceer een GitHub Release met een vierdelige tag zoals v1.2.3.4. Ieder onderdeel moet tussen 0 en 65534 liggen. De workflow gebruikt die waarde voor de applicatie en containertag en publiceert daarnaast sha-COMMIT- en latest-tags.

Lokale builds hebben standaard versie 0.0.0.0. GitHub Actions zijn op volledige commit-SHA's vastgezet en Dependabot bewaakt NuGet, Docker en GitHub Actions.

## Kosten, privacy en verantwoordelijkheid

De broncode en het publieke GHCR-image kunnen kosteloos worden verkregen, maar Somtoday-toegang en Azure-resources zijn niet gegarandeerd gratis. Container Apps-verbruik boven een eventuele gratis hoeveelheid, Key Vault, Blob Storage en Log Analytics kunnen kosten veroorzaken.

De applicatie verwerkt gegevens van leerlingen, medewerkers en mogelijk ouders of verzorgers. De deployende school of haar Azure-partner is verantwoordelijk voor doelbinding, grondslag, verwerkersafspraken, toegangsbeheer, bewaartermijnen, beveiliging, dataminimalisatie en overige AVG/GDPR-verplichtingen. Controleer gegenereerde CSV-bestanden en Microsoft SDS-instellingen vóór productiegebruik.

## Licentie en bijdragen

Somtoday2MicrosoftSDS is beschikbaar onder [GNU AGPL v3 of later](LICENSE). Zie [NOTICE.md](NOTICE.md), [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), [SECURITY.md](SECURITY.md) en [CONTRIBUTING.md](CONTRIBUTING.md).

Dit project is afgeleid van [SomtodayOpenAPI2MicrosoftSchoolDataSync](https://github.com/Essella/SomtodayOpenAPI2MicrosoftSchoolDataSync).
