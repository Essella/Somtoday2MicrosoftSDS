using Microsoft.Extensions.Configuration;

namespace Somtoday2MicrosoftSDS.Helpers;

internal sealed record SyncConfiguration(
    Guid[] SchoolUuids,
    Guid InboundFlowId,
    string ClientId,
    string ClientSecret,
    SomEnvironmentConfig SomEnvironment,
    string[] IncludedLocationCodes,
    string[] ExcludedLocationCodes,
    bool EnableGuardianSync)
{
    internal static bool TryCreate(
        IConfiguration configuration,
        bool isDevelopment,
        out SyncConfiguration value,
        out string[] errors)
    {
        List<string> validationErrors = [];
        string[] configuredSchoolUuids = configuration.GetSection("Somtoday:SchoolUUID").Get<string[]>() ?? [];
        List<Guid> schoolUuids = [];
        HashSet<Guid> uniqueSchoolUuids = [];

        if (configuredSchoolUuids.Length == 0)
        {
            validationErrors.Add("Somtoday:SchoolUUID must be a non-empty array");
        }

        foreach (string configuredSchoolUuid in configuredSchoolUuids)
        {
            if (!Guid.TryParse(configuredSchoolUuid, out Guid schoolUuid) || schoolUuid == Guid.Empty)
            {
                validationErrors.Add($"Somtoday:SchoolUUID contains an invalid or empty UUID: '{configuredSchoolUuid}'");
            }
            else if (!uniqueSchoolUuids.Add(schoolUuid))
            {
                validationErrors.Add($"Somtoday:SchoolUUID contains a duplicate UUID: '{schoolUuid}'");
            }
            else
            {
                schoolUuids.Add(schoolUuid);
            }
        }

        string configuredInboundFlowId = configuration["SchoolDataSync:InboundFlowId"];
        if (!Guid.TryParse(configuredInboundFlowId, out Guid inboundFlowId) || inboundFlowId == Guid.Empty)
        {
            validationErrors.Add("SchoolDataSync:InboundFlowId must be a valid non-empty UUID");
        }

        string clientId = configuration["Somtoday:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            validationErrors.Add("Somtoday:ClientId is missing");
        }

        string clientSecret = configuration["Somtoday:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            validationErrors.Add("Somtoday:ClientSecret is missing");
        }

        SomEnvironmentConfig somEnvironment = ParseEnvironment(
            configuration["Somtoday:Environment"],
            validationErrors);
        if (!isDevelopment && ReferenceEquals(somEnvironment, SomEnvironmentConfig.Nightly))
        {
            validationErrors.Add("Somtoday:Environment NIGHTLY is allowed only in Development");
        }

        value = validationErrors.Count == 0
            ? new SyncConfiguration(
                schoolUuids.ToArray(),
                inboundFlowId,
                clientId.Trim(),
                clientSecret.Trim(),
                somEnvironment,
                configuration.GetSection("Locations:IncludedLocationCodes").Get<string[]>() ?? [],
                configuration.GetSection("Locations:ExcludedLocationCodes").Get<string[]>() ?? [],
                configuration.GetValue<bool>("SchoolDataSync:EnableGuardianSync"))
            : null;

        errors = validationErrors.ToArray();
        return validationErrors.Count == 0;
    }

    private static SomEnvironmentConfig ParseEnvironment(string configuredEnvironment, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(configuredEnvironment))
        {
            errors.Add("Somtoday:Environment is missing");
            return SomEnvironmentConfig.Prod;
        }

        return char.ToLowerInvariant(configuredEnvironment.Trim()[0]) switch
        {
            'a' => SomEnvironmentConfig.Acceptatie,
            'n' => SomEnvironmentConfig.Nightly,
            't' => SomEnvironmentConfig.Test,
            'p' => SomEnvironmentConfig.Prod,
            _ => AddInvalidEnvironmentError(configuredEnvironment, errors)
        };
    }

    private static SomEnvironmentConfig AddInvalidEnvironmentError(string configuredEnvironment, List<string> errors)
    {
        errors.Add($"Somtoday:Environment is invalid: '{configuredEnvironment}'");
        return SomEnvironmentConfig.Prod;
    }
}
