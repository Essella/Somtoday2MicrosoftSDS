# Somtoday2MicrosoftSDS

Somtoday2MicrosoftSDS is een eenmalig uitgevoerde .NET 10-batchapplicatie die actuele gegevens uit [Somtoday Connect](https://som.today/partnerprogramma/) omzet naar CSV-datasets voor [Microsoft School Data Sync](https://learn.microsoft.com/en-us/schooldatasync/data-ingestion-with-sds-v2.1-csv) (SDS) V1 en V2.1 en deze publiceert in Azure Blob Storage.

De enige ondersteunde productievorm is een geplande Azure Container Apps Job. De bedoelde beheerders zijn schoolbeheerders en Azure-partners die namens scholen werken. Lokaal uitvoeren is alleen bedoeld voor ontwikkeling en tests; er is geen ondersteunde Windows-executable.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FEssella%2FSomtoday2MicrosoftSDS%2Fmain%2Finfra%2Fazuredeploy.json)

## Begin hier

Gebruik de documentatie die bij uw taak hoort:

| Taak | Documentatie |
|---|---|
| Configuratie, locaties, gebruikersnaamregels of secrets | [Configuratiehandleiding](docs/operations/CONFIGURATION.md) |
| Inhoud van V1/V2.1, klassen, personen of guardians | [Exportcontract](docs/contracts/EXPORT.md) |
| Mappen, samenvoegen, staging, retries, rollback of opruimen | [Publicatiecontract](docs/contracts/PUBLICATION.md) |
| Azure-resources, identity, planning of uitrollen | [Azure-deploymenthandleiding](docs/operations/DEPLOYMENT.md) |
| Images, versies of releases | [Releasehandleiding](docs/operations/RELEASES.md) |
| Bekende verschillen tussen bedoeling en implementatie | [Afwijkingenregister](docs/DEVIATIONS.md) |
| Ontwikkelen of bijdragen | [Projectkern](docs/PROJECT_CORE.md), [ontwikkelhandleiding](docs/DEVELOPMENT.md) en [CONTRIBUTING.md](CONTRIBUTING.md) |
| Beveiliging, privacy of melden van een kwetsbaarheid | [SECURITY.md](SECURITY.md) |

De [projectkern](docs/PROJECT_CORE.md) en de gerichte contracten zijn de leidende bronnen voor bedoeld gedrag. Code en tests kunnen daarvan afwijken; iedere bekende afwijking heeft een stabiel ID in het afwijkingenregister.

## Vereisten

- Somtoday Connect-client-ID, clientsecret en minstens één instellings-UUID. Toegang is gebonden aan de voorwaarden van Somtoday; zie het [Somtoday-partnerprogramma](https://som.today/partnerprogramma/).
- Een Azure-abonnement en rechten om resources en roltoewijzingen te maken.
- Voor lokale ontwikkeling: de .NET 10 SDK.

De publieke productielijst met instellingen is zonder authenticatie beschikbaar via [https://api.somtoday.nl/rest/v1/connect/instelling](https://api.somtoday.nl/rest/v1/connect/instelling). Deze lijst is de gezaghebbende bron voor `Instelling.Afkorting`, ook wanneer synchronisatiegegevens uit TEST, ACCEPTATIE of de uitsluitend voor Development toegestane NIGHTLY-omgeving komen. NIGHTLY gebruikt HTTP voor schooldata en mag niet met echte persoonsgegevens worden gebruikt.

```powershell
Invoke-WebRequest -Uri 'https://api.somtoday.nl/rest/v1/connect/instelling'
```

Houd rekening met de door Somtoday gestelde tijdvensters en gebruiksvoorwaarden voor het synchroniseren van leerlinggegevens.

## Implementeren en gebruiken

`infra/main.bicep` maakt de benodigde Blob Storage, Log Analytics en geplande Container Apps Job met managed identity. Het Somtoday-clientsecret is een secret van de Job. Gebruik de knop hierboven of volg de [Azure-deploymenthandleiding](docs/operations/DEPLOYMENT.md). Pin productie op een releasetag of image-digest in plaats van `latest`.

De applicatie ondersteunt meerdere Somtoday-instellingen, locatie-inclusie en -exclusie, afzonderlijke gebruikersnaamregels voor docenten en leerlingen, optionele guardian-relaties en bestanden met alleen headers. Uitvoer kan onafhankelijk per instelling en per vestiging worden gegroepeerd; standaard krijgt iedere instelling één V1- en één V2.1-dataset waarin de geselecteerde vestigingen zijn samengevoegd. Voor iedere geplande scope probeert de applicatie altijd beide SDS-versies met dezelfde populatie te genereren; er bestaat geen instelling of opdrachtregeloptie om V1 of V2.1 uit te sluiten. Zie de [configuratiehandleiding](docs/operations/CONFIGURATION.md) en het [publicatiecontract](docs/contracts/PUBLICATION.md) voor alle vier indelingen en conflictregels.

Docenten en leerlingen worden uitsluitend aan bestaande Microsoft-accounts gekoppeld; configureer SDS niet om ontbrekende gebruikers aan te maken. Guardians worden alleen geëxporteerd met toestemming voor e-mailcontact, een e-mailadres, een relatie met een inbegrepen leerling en niet-lege `Voorletters` en `Achternaam`. Nederlandse telefoonnummers met trunkprefix of zonder prefix worden permissief genormaliseerd: zowel `0612345678` als `612345678` wordt `+31612345678`; een uiteindelijk ongeldig nummer blijft leeg zonder de guardianrelatie te verwijderen.

Alleen het exacte argument `--empty-csv` heeft case-insensitief effect. Andere opdrachtregelargumenten worden genegeerd en kunnen configuratie niet overschrijven. De Dynamic-LINQ-expressies voor gebruikersnamen zijn vertrouwde beheerdersconfiguratie, geen sandbox. Gebruik daarin beleidsmatig geen BSN/ECK, telefoon, geboortedatum of andere gevoelige modelwaarden.

Een Blob-service-URI moet in iedere omgeving een absolute HTTPS-URI zijn. Gebruik voor een lokale HTTP-emulator zoals Azurite uitsluitend de Development-connectionstring. Een geselecteerde vestiging zonder bruikbare afkorting wordt niet stil overgeslagen, maar laat alleen de betrokken instelling mislukken; andere instellingen blijven verwerkbaar.

Wanneer dezelfde docent of leerling bij meerdere vestigingen hoort, bevat V1 bewust één persoonsrij per vestiging met dezelfde `SIS ID` en een andere `School SIS ID`. Microsoft documenteert `SIS ID` als uniek en kan zo'n gegroepeerde V1-dataset daarom afkeuren. V2.1 neemt de persoon eenmaal op en koppelt die aan meerdere organisaties.

De V1-bestandsnamen behouden exact de bestaande, door SDS geaccepteerde schrijfwijze: `School.csv`, `Section.csv`, `Teacher.csv`, `Student.csv`, `TeacherRoster.csv`, `StudentEnrollment.csv` en, bij guardian-sync, `User.csv` en `Guardianrelationship.csv`.

Tijdens een normale run neemt de applicatie een vestiging alleen op wanneer minstens één klas na UUID-resolutie zowel een docent als een leerling bevat. Bij een samengevoegde scope worden niet-geschikte vestigingen weggelaten terwijl de overige vestigingen wel worden gepubliceerd. Alleen wanneer de volledige scope geen geschikte vestiging bevat, blijft eerder gepubliceerde Blob-output ongewijzigd. De expliciete of automatische modus voor bestanden met alleen headers publiceert iedere geplande scope.

Als één instelling niet publiek kan worden gematcht, geen geldige uitvoerpaden oplevert of de brongegevens niet kunnen worden opgehaald, publiceert een samengevoegde scope de succesvol opgeloste instellingen en eindigt de run met exitcode `1`. Een conversie- of uploadfout blokkeert de betrokken SDS-versie en markeert alle deelnemende instellingen als mislukt, maar verhindert de verplichte poging voor de andere versie of een volgende scope niet. Als de publieke productielijst met instellingen zelf niet bereikbaar is, wordt geen SDS-dataset gepubliceerd.

Somtoday-authenticatie krijgt maximaal vier pogingen en wordt alleen herhaald bij netwerk- of HTTP-timeouts, HTTP 408, HTTP 429 en HTTP 5xx. Overige clientfouten en ongeldige tokenantwoorden stoppen direct voor de betrokken instelling.

Iedere volledige CSV-set wordt eerst in geheugen gemaakt en naar `{Output:Folder}/.staging/{RunId}/{FileName}` geschreven, waarbij `RunId` één compacte UUIDv7 voor de volledige applicatierun is. Er is dus één gedeelde stagingmap direct onder de outputfolder en geen aparte V1- of V2.1-stagingmap. Iedere dataset overschrijft vóór promotie zijn eigen stagingbestanden; staging is alleen tijdelijke werkdata voor de actuele run en dataset en is nooit een bron voor rollback of een latere run. Daarna kopieert de applicatie de set intern naar de live bestandsnamen, met één eerste promotiepoging en maximaal drie volledige herpogingen. Na vier mislukte promoties herstelt zij de nieuwste oudere, volledige set uit geldige live basisblobs en Blob-versies die aantoonbaar door deze applicatie zijn gepubliceerd. Handmatig geplaatste bestanden zonder applicatiemetadata worden normaal overschreven, maar nooit als automatische herstelbron gebruikt. Als geen volledige herstelset bestaat of herstel mislukt, stopt de gehele run; bij geslaagd herstel kunnen volgende datasets doorgaan.

Staging-opruiming krijgt zowel bij startup als na iedere dataset één poging plus drie volledige herpogingen. Iedere poging probeert alle toepasselijke Blobs. Als opruimen daarna nog mislukt, volgt een waarschuwing maar gaat de run door; een succesvolle publicatie blijft succesvol en live output wordt niet teruggedraaid. Restanten kunnen bij een volgende startup opnieuw worden geprobeerd.

De Azure-template schakelt Blob-versioning in en bewaart vorige live versies via een lifecycle-regel zeven dagen. Actuele stagingblobs en stagingversies worden onder de genormaliseerde outputfolder na meer dan één dag lifecycle-eligible voor verwijdering. Azure Lifecycle Management werkt asynchroon; dit is geen exact verwijdermoment. De bestaande soft-deleteperiode van zeven dagen kan verwijderde Blobs en versies daarna nog tijdelijk herstelbaar houden, maar staging heeft geen functionele herstelwaarde. Guardian-sync aan publiceert de guardianbestanden ook als die alleen headers bevatten; guardian-sync uit verwijdert de bekende guardianbestanden bij promotie. Gelijktijdige of overlappende applicatieruns worden niet ondersteund.

## Doorzetten naar School Data Sync met Power Automate

De publieke Power Automate-template [CSV-upload van School Data Sync automatiseren via SFTP](https://make.powerautomate.com/galleries/public/templates/3c1ff79158b34374b9ee3c683abb5b55/) kan als uitgangspunt dienen. Deze repository implementeert of beheert de Power Automate-flow en de Microsoft SDS-import niet.

[![Power Automate: Automate School Data Sync CSV Upload via SFTP](./Power%20Automate%20School%20Data%20Sync%20SDS%20Upload%20SFTP.jpg)](https://make.powerautomate.com/galleries/public/templates/3c1ff79158b34374b9ee3c683abb5b55/)

Gebruik voor de door deze deployment aangemaakte Storage-account bij voorkeur de [Azure Blob Storage-connector](https://learn.microsoft.com/en-us/connectors/azureblob/) in plaats van SFTP-SSH. Alleen de verbinding in de SFTP-template vervangen is niet voldoende:

1. Maak een eigen kopie van de publieke template en controleer dat deze de nieuwe `Microsoft School Data Sync V2`-connector gebruikt, niet SDS Classic. De connectornaam verwijst naar de nieuwe SDS-ervaring; de flow kan zowel het V1- als het V2.1-formaat verwerken.
2. Vervang SFTP zoeken/lezen door `Lists blobs (V2)` en `Get blob content using path (V2)` van de Azure Blob Storage-connector.
3. Koppel per flow precies één volledige live V1- of V2.1-map aan een SDS Connect data-Flow ID dat voor hetzelfde formaat is geconfigureerd. Meng de formaten nooit en upload iedere keer dezelfde volledige bestandenset als bij de initiële SDS-upload.
4. Sluit `{Output:Folder}/.staging/` uit, gebruik geen trigger per afzonderlijke Blob en start pas nadat de Container Apps Job succesvol is afgerond.

De huidige Bicep- en ARM-template zijn hiermee compatibel onder deze voorwaarden:

- Maak de connectorverbinding met Microsoft Entra ID of service-principalauthenticatie en gebruik alleen V2-acties. Authenticatie met een Storage-accountkey werkt niet, omdat shared-keytoegang bewust is uitgeschakeld.
- Geef de gebruiker of service principal van de Power Automate-verbinding handmatig minimaal `Storage Blob Data Reader` op de uitvoercontainer. De Azure-template kent alleen de Container Apps Job een Blob-gegevensrol toe en maakt geen Power Automate-verbinding of -roltoewijzing.
- Configureer de volledige Blob-service-URI, de privécontainer en het gekozen live V1- of V2.1-uitvoerpad. De Azure Blob Storage-connector is een Premium-connector.
- Voeg voor deze flow geen Storage-firewall toe. Microsoft ondersteunt Power Platform-verbindingen met deze connector naar een Storage-account achter een firewall niet betrouwbaar; IP-allowlisting verandert dat niet.

Azure Blob Storage als SFTP-server is hier geen geschikt alternatief. De [SFTP-SSH-connector ondersteunt Azure Blob Storage SFTP niet](https://learn.microsoft.com/en-us/connectors/sftpwithssh/#general-known-issues-and-limitations). Bovendien vereist [SFTP op Azure Blob Storage](https://learn.microsoft.com/en-us/azure/storage/blobs/secure-file-transfer-protocol-support) een hiërarchische namespace, terwijl die niet samengaat met de voor veilige publicatie benodigde [Blob-versioning](https://learn.microsoft.com/en-us/azure/storage/blobs/versioning-overview). Zie de [Azure-deploymenthandleiding](docs/operations/DEPLOYMENT.md#power-automate-school-data-sync-handoff) voor de operationele aandachtspunten.

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
