using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Somtoday2MicrosoftSDS.Models;

namespace Somtoday2MicrosoftSDS.Helpers
{
    internal class OpenAPIHelper
    {
        private readonly string clientId;
        private readonly string clientSecret;
        private readonly Guid schoolUUID;
        private readonly SomEnvironmentConfig somConfig;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly ILogger<OpenAPIHelper> _logger;
        private SomOpenApiClient somOpenApiClient;

        public bool IsConnected { get; private set; }

        public OpenAPIHelper(string clientId, string clientSecret, Guid schoolUUID, SomEnvironmentConfig somConfig, IHttpClientFactory httpClientFactory, ILogger<OpenAPIHelper> logger = null)
        {
            this.clientId = clientId;
            this.clientSecret = clientSecret;
            this.schoolUUID = schoolUUID;
            this.somConfig = somConfig;
            this.httpClientFactory = httpClientFactory;
            _logger = logger ?? LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<OpenAPIHelper>();
        }

        internal async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                IsConnected = false;
                using HttpClient authenticationClient = httpClientFactory.CreateClient();
                using FormUrlEncodedContent content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret
                });
                using HttpResponseMessage response = await authenticationClient.PostAsync(
                    somConfig.LoginUrl + schoolUUID,
                    content,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    string accessToken = JObject.Parse(responseContent)["access_token"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(accessToken))
                    {
                        _logger.LogWarning("Somtoday authentication succeeded without an access token");
                        return;
                    }

                    HttpClient httpClient = httpClientFactory.CreateClient();
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    somOpenApiClient = new SomOpenApiClient(httpClient)
                    {
                        BaseUrl = somConfig.Url
                    };
                    IsConnected = true;
                    _logger.LogInformation("Successfully connected to Somtoday API");
                }
                else
                {
                    _logger.LogWarning(
                        "Somtoday authentication failed with HTTP status {StatusCode}",
                        (int)response.StatusCode);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError(
                    "Error connecting to Somtoday API ({Error})",
                    SafeExceptionSummary.Create(e));
            }
        }

        internal async Task<Instelling> GetInstellingAsync(CancellationToken cancellationToken = default)
        {
            InstellingResponse response = await somOpenApiClient.InstellingAsync(null, null, cancellationToken);
            return SelectInstitution(response.Instellingen, schoolUUID);
        }

        internal static Instelling SelectInstitution(IEnumerable<Instelling> institutions, Guid schoolUuid)
        {
            Instelling[] matchingInstitutions = institutions
                .Where(instelling => instelling.Uuid == schoolUuid)
                .ToArray();

            if (matchingInstitutions.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one Somtoday institution for UUID {schoolUuid}, but found {matchingInstitutions.Length}");
            }

            if (string.IsNullOrWhiteSpace(matchingInstitutions[0].Afkorting))
            {
                throw new InvalidOperationException($"Somtoday institution {schoolUuid} has no abbreviation");
            }

            return matchingInstitutions[0];
        }

        internal async Task<List<Vestiging>> GetSelectedVestigingenAsync(
            string[] includedLocationCodes,
            string[] excludedLocationCodes,
            CancellationToken cancellationToken = default)
        {
            List<Vestiging> vestigingen = await GetVestigingenAsync(cancellationToken);
            return LocationSelector.Select(vestigingen, includedLocationCodes, excludedLocationCodes);
        }

        internal async Task<List<VestigingModel>> DownloadAllInfoAsync(
            IEnumerable<Vestiging> vestigingen,
            bool enableGuardianSync,
            CancellationToken cancellationToken = default)
        {
            List<VestigingModel> result = [];

            foreach (Vestiging vestiging in vestigingen)
            {
                _logger?.LogDebug("Processing vestiging: {VestigingNaam}", vestiging.Naam);

                Task<List<Lesgroep>> lesgroepenTask = GetLesgroepenAsync(vestiging, cancellationToken);
                Task<List<Medewerker>> medewerkersTask = GetTeacherInfoAsync(vestiging, cancellationToken);
                Task<List<Leerling>> leerlingenTask = GetStudentInfoAsync(vestiging, cancellationToken);
                Task<List<OuderVerzorger>> oudersTask = enableGuardianSync
                    ? GetGuardianInfoAsync(vestiging, cancellationToken)
                    : Task.FromResult(new List<OuderVerzorger>());

                await Task.WhenAll(lesgroepenTask, medewerkersTask, leerlingenTask, oudersTask);

                List<Lesgroep> lesgroepen = await lesgroepenTask;
                List<Medewerker> medewerkers = await medewerkersTask;
                List<Leerling> leerlingen = await leerlingenTask;
                List<OuderVerzorger> ouders = await oudersTask;

                result.Add(new VestigingModel
                {
                    Vestiging = vestiging,
                    Lesgroepen = lesgroepen,
                    Leerlingen = leerlingen,
                    Medewerkers = medewerkers,
                    OuderVerzorgers = ouders
                });
            }

            return result;
        }

        private async Task<List<Vestiging>> GetVestigingenAsync(CancellationToken cancellationToken)
        {
            try
            {
                VestigingResponse vestigingenResponse = await somOpenApiClient.VestigingAsync(null, null, cancellationToken);
                return vestigingenResponse.Vestigingen.ToList();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                _logger?.LogError(
                    "Error retrieving vestigingen from Somtoday ({Error})",
                    SafeExceptionSummary.Create(e));
                throw;
            }
        }

        internal async Task<List<Lesgroep>> GetLesgroepenAsync(Vestiging vestiging, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Fetching lesgroepen...");
            List<Lesgroep> lesgroepen = [];
            while (true)
            {
                LesgroepResponse lesgroepenResponse = await somOpenApiClient.LesgroepAsync(null, Peilschooljaar13.HUIDIG, vestiging.Uuid, lesgroepen.Count, 100, null, null, cancellationToken);

                if (lesgroepenResponse.Lesgroepen.Count == 0)
                {
                    return lesgroepen;
                }

                _logger?.LogDebug("Lesgroepen count: {Count}", lesgroepen.Count);
                lesgroepen.AddRange(lesgroepenResponse.Lesgroepen);
            }
        }

        internal async Task<List<Medewerker>> GetTeacherInfoAsync(Vestiging vestiging, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Fetching teachers for vestiging: {VestigingName}", vestiging.Naam);
            List<Medewerker> medewerkers = [];
            while (true)
            {
                MedewerkerResponse medewerkerResponse = await somOpenApiClient.MedewerkerAsync(Peilschooljaar11.HUIDIG, null, vestiging.Uuid, medewerkers.Count, 100, null, null, cancellationToken);

                if (medewerkerResponse.Medewerkers.Count == 0)
                {
                    return medewerkers;
                }

                _logger?.LogDebug("Teachers count: {Count}", medewerkers.Count);
                medewerkers.AddRange(medewerkerResponse.Medewerkers);
            }
        }

        internal async Task<List<Leerling>> GetStudentInfoAsync(Vestiging vestiging, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Fetching students for vestiging: {VestigingName}", vestiging.Naam);
            List<Leerling> leerlingen = [];
            while (true)
            {
                LeerlingResponse leerlingenResponse = await somOpenApiClient.LeerlingAsync(null, Peilschooljaar.HUIDIG, vestiging.Uuid, leerlingen.Count, 100, null, null, cancellationToken);

                if (leerlingenResponse.Leerlingen.Count == 0)
                {
                    return leerlingen;
                }

                _logger?.LogDebug("Students count: {Count}", leerlingen.Count);
                leerlingen.AddRange(leerlingenResponse.Leerlingen);
            }
        }

        internal async Task<List<OuderVerzorger>> GetGuardianInfoAsync(Vestiging vestiging, CancellationToken cancellationToken)
        {
            _logger?.LogDebug("Fetching guardians for vestiging: {VestigingName}", vestiging.Naam);
            List<OuderVerzorger> ouders = [];
            while (true)
            {
                OuderVerzorgerResponse oudersResponse = await somOpenApiClient.OuderVerzorgerAsync(null, Peilschooljaar18.HUIDIG, vestiging.Uuid, ouders.Count, 100, null, null, cancellationToken);

                if (oudersResponse.OuderVerzorgers.Count == 0)
                {
                    return ouders;
                }

                _logger?.LogDebug("Guardians count: {Count}", ouders.Count);
                ouders.AddRange(oudersResponse.OuderVerzorgers);
            }
        }

        private async Task<List<Account>> GetAccountInfoAsync(Vestiging vestiging, CancellationToken cancellationToken)
        {
            List<Account> accounts = [];
            List<Account> accountsMedewerkers = [];
            List<Account> accountsLeerlingen = [];

            while (true)
            {
                AccountResponse medewerkerAccounts = await somOpenApiClient.AccountGET3Async(null, vestiging.Uuid, 0, accountsMedewerkers.Count, null, cancellationToken);
                if (medewerkerAccounts.Accounts.Count == 0)
                {
                    break;
                }

                accountsMedewerkers.AddRange(medewerkerAccounts.Accounts);
            }

            while (true)
            {
                AccountResponse leerlingAccounts = await somOpenApiClient.AccountGETAsync(null, Peilschooljaar7.HUIDIG, vestiging.Uuid, 0, accountsLeerlingen.Count, null, cancellationToken);
                if (leerlingAccounts.Accounts.Count == 0)
                {
                    break;
                }

                accountsLeerlingen.AddRange(leerlingAccounts.Accounts);
            }

            accounts.AddRange(accountsMedewerkers);
            accounts.AddRange(accountsLeerlingen);
            return accounts;
        }
    }
}
