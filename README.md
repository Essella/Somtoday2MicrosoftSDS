# SyncIdPreview: Somtoday Connect naar Microsoft School Data Sync

Open source console-applicatie om CSV-bestanden voor Microsoft School Data Sync te maken met gegevens uit [Somtoday](https://www.som.today/) via Somtoday Connect.

De CSV-bestanden zijn nodig omdat een directe koppeling met Somtoday niet mogelijk is. Somtoday biedt op dit moment geen ondersteuning voor de OneRoster-standaard: https://www.imsglobal.org/oneroster-v11-final-specification. Zodra Somtoday dat wel doet, is deze applicatie waarschijnlijk overbodig.

![Logo](/SyncIdPreview/Resources/SOMSDS.ico)

## Huidige solution

Deze repository bevat momenteel een .NET console-app:

* Solution: `SyncIdPreview.sln`
* Project: `SyncIdPreview/SyncIdPreview.csproj`
* Uitvoerbare applicatie: `SyncIdPreview.exe`
* Configuratiebestand: `appsettings.json`
* Target framework: `.NET 10.0`

## Functionaliteiten

* Haalt lesgroepen, leerlingen, medewerkers en optioneel ouders/verzorgers op via Somtoday Connect.
* Verwerkt lesgroepen van het huidige schooljaar.
* Lesgroepen zonder docent worden niet verwerkt.
* Lesgroepen zonder leerling worden niet verwerkt.
* Lesgroepen krijgen een uniek ID op basis van de vestigingsafkorting. Dit zie je niet terug in de displaynaam van de lesgroep.
* Ongeldige tekens worden vervangen volgens de Microsoft-richtlijn: https://support.microsoft.com/en-us/kb/905231.
* Maakt SDS CSV-bestanden in V1- of V2.1-formaat.
* Kan output naar disk schrijven of direct naar Azure Blob Storage.
* Kan lege SDS CSV-bestanden met alleen headers genereren, bijvoorbeeld voor het einde van het schooljaar.
* Logt via `Microsoft.Extensions.Logging` naar de console.

## Installatie

Download een release ZIP via https://github.com/DwayneSelsig/SomtodayOpenAPI2MicrosoftSchoolDataSync/releases en pak de bestanden uit.

Je kunt de applicatie ook zelf bouwen:

```powershell
dotnet restore SyncIdPreview.sln
dotnet build SyncIdPreview.sln -c Release
dotnet publish SyncIdPreview\SyncIdPreview.csproj -c Release -o .\publish
```

Start daarna `SyncIdPreview.exe` vanuit de publicatiemap. Voor periodieke synchronisatie kun je een scheduled task aanmaken. Let op: het synchroniseren van leerlinggegevens is alleen 's nachts toegestaan vanuit Somtoday.

## Configuratie

Bewerk `appsettings.json` in de installatiemap of publicatiemap. De applicatie gebruikt de volgende configuratiestructuur:

```json
{
  "Somtoday": {
    "Environment": "PROD",
    "SchoolUUID": "00000000-0000-0000-0000-000000000000",
    "ClientId": "00000000-0000-0000-0000-000000000000",
    "ClientSecret": "00000000-0000-0000-0000-000000000000"
  },
  "Locations": {
    "FilterByLocation": false,
    "IncludedLocationCodes": [ "ab", "cd" ],
    "SeparateOutputFolderForEachLocation": false
  },
  "Storage": {
    "Mode": "Disk",
    "AzureBlob": {
      "ConnectionString": "",
      "Container": "sds"
    }
  },
  "Output": {
    "Folder": "C:\\SchoolDataSync\\CSV\\",
    "GenerateEmptyCsv": false,
    "ClearCsvAtYearEnd": false
  },
  "UsernameFormat": {
    "Teacher": "Emailadres",
    "Student": "Emailadres"
  },
  "SchoolDataSync": {
    "EnableGuardianSync": false,
    "CsvVersion": 1
  }
}
```

### Somtoday

`Somtoday:Environment` bepaalt met welke Somtoday-omgeving wordt verbonden. Ondersteunde waarden zijn:

* `PROD`: productieomgeving.
* `TEST`: testomgeving.
* `ACCEPTATIE`: acceptatieomgeving.
* `NIGHTLY`: nightly-omgeving, alleen intern beschikbaar.

De applicatie kijkt naar de eerste letter, dus `PROD`, `Productie` en `p` leiden allemaal naar productie.

`Somtoday:SchoolUUID` is het organisatie-UUID. Dit kun je opvragen via:

https://api.somtoday.nl/rest/v1/connect/instelling

`Somtoday:ClientId` en `Somtoday:ClientSecret` ontvang je via het Somtoday Connect Partnerprogramma:

https://som.today/somtoday-connect/contact-partnerprogramma-somtoday-connect/

Log of deel `ClientSecret` nooit.

### Locations

`Locations:FilterByLocation` bepaalt of alle vestigingen worden opgehaald of alleen de vestigingen uit `IncludedLocationCodes`.

* `false`: alle vestigingen ophalen. Dit is de aanbevolen standaard.
* `true`: alleen de opgegeven vestigingen ophalen.

`Locations:IncludedLocationCodes` is een JSON-array met vestigingsafkortingen. De waarden zijn niet hoofdlettergevoelig. Deze instelling is verplicht wanneer `FilterByLocation` op `true` staat.

`Locations:SeparateOutputFolderForEachLocation` bepaalt of de output per vestiging wordt gescheiden.

* `false`: alle gegevens worden gecombineerd in dezelfde outputmap of blob-prefix.
* `true`: per vestiging wordt een aparte submap of blob-prefix gebruikt.

### Storage

`Storage:Mode` bepaalt waar de CSV-bestanden worden opgeslagen.

* `Disk`: schrijf de CSV-bestanden naar `Output:Folder`.
* `AzureBlob`: schrijf de CSV-bestanden naar Azure Blob Storage.

Bij `Disk` bepaalt `SchoolDataSync:CsvVersion` of V1- of V2.1-bestanden worden geschreven.

Bij `AzureBlob` worden altijd beide SDS-formaten geschreven: V1 en V2.1. De waarde van `SchoolDataSync:CsvVersion` wordt dan genegeerd.

Voor Azure Blob Storage zijn deze instellingen verplicht:

* `Storage:AzureBlob:ConnectionString`
* `Storage:AzureBlob:Container`

De standaardcontainer is `sds`. Log of deel de connection string nooit.

Bij Azure Blob Storage gebruikt de applicatie deze structuur:

* `config/`
* `logs/`
* `sds/temp/`
* `sds/output/v1/`
* `sds/output/v2/`

Met `SeparateOutputFolderForEachLocation=true` wordt per vestiging een submap gebruikt:

* `sds/output/v1/{VestigingAfkorting}/`
* `sds/output/v2/{VestigingAfkorting}/`

Securityadvies voor SFTP- of Blob-toegang: geef gebruikers alleen read/list op `sds/output/v1/` en/of `sds/output/v2/`. Geef ze geen toegang tot `config/`, `logs/` of `sds/temp/`.

### Output

`Output:Folder` is alleen verplicht bij `Storage:Mode=Disk`.

Voorbeeld:

```text
C:\SchoolDataSync\CSV\
```

`Output:GenerateEmptyCsv` bepaalt of de applicatie geen Somtoday-data ophaalt, maar alleen lege SDS CSV-bestanden met headers maakt.

* `false`: data ophalen, converteren en opslaan. Dit is de aanbevolen standaard.
* `true`: lege CSV-bestanden met headers genereren.

Je kunt dit ook eenmalig starten zonder de configuratie blijvend aan te passen:

```powershell
SyncIdPreview.exe --empty-csv
```

`Output:ClearCsvAtYearEnd` genereert op 31 juli lege SDS CSV-bestanden met alleen headers.

* `false`: op 31 juli normaal data ophalen en CSV-bestanden maken.
* `true`: op 31 juli lege CSV-bestanden met headers maken.

### UsernameFormat

`UsernameFormat:Teacher` en `UsernameFormat:Student` bepalen welke waarde uit Somtoday als gebruikersnaam in de SDS-bestanden komt. De standaardwaarde is `Emailadres`.

Je kunt een eenvoudige propertynaam gebruiken:

```json
"Teacher": "Emailadres"
```

De applicatie zet dit intern om naar:

```text
{user.Emailadres}
```

Je kunt ook zelf een template gebruiken, bijvoorbeeld:

```json
"Student": "{user.Emailadres}"
```

Controleer beschikbare attributen in de Somtoday OpenAPI-specificatie:

https://editor.swagger.io/?url=https://api.somtoday.nl/rest/v1/connect/documented/openapi

### SchoolDataSync

`SchoolDataSync:EnableGuardianSync` bepaalt of ouder/verzorgergegevens worden meegenomen.

* `false`: ouders/verzorgers niet synchroniseren. Dit is de aanbevolen standaard.
* `true`: extra CSV-data voor ouders/verzorgers genereren.

Let op: leerlingen ouder dan 18 jaar kunnen ervoor kiezen dat ouders geen inzage hebben in hun schoolprestaties. Omdat Somtoday deze keuze niet doorgeeft, moet de wekelijkse samenvatting per e-mail voor iedereen uitgeschakeld blijven. Standaard staat deze e-mail uit. Zie:

https://docs.microsoft.com/en-us/MicrosoftTeams/expand-teams-across-your-org/assignments-in-teams#weekly-guardian-email-digest

`SchoolDataSync:CsvVersion` bepaalt het SDS CSV-formaat bij disk-output.

* `1`: SDS CSV V1: https://aka.ms/sdsV1csv
* `2`: SDS CSV V2.1: https://aka.ms/sdsV2dot1

School Data Sync accepteert beide formaten. Voor nieuwe inrichting ligt V2.1 meestal het meest voor de hand.

## Uitvoeren

Start de applicatie vanuit de map waar `SyncIdPreview.exe` en `appsettings.json` staan:

```powershell
.\SyncIdPreview.exe
```

Of run vanuit de broncode:

```powershell
dotnet run --project SyncIdPreview\SyncIdPreview.csproj
```

Lege CSV-bestanden genereren:

```powershell
.\SyncIdPreview.exe --empty-csv
```

## Volgende stappen

Upload of koppel de CSV-bestanden aan Microsoft School Data Sync:

https://learn.microsoft.com/en-us/schooldatasync/data-ingestion-with-sds-v2.1-csv

## Koppelen met Magister

Gebruikt jouw school Magister en zoek je een koppeling tussen Magister en School Data Sync? Bezoek dan:

https://github.com/sikkepitje/TeamSync
