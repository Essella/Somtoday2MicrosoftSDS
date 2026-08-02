namespace Somtoday2MicrosoftSDS.Helpers;

internal static class GuardianExportPolicy
{
    internal static bool HasUsableContact(OuderVerzorger guardian)
    {
        return guardian.WenstContactViaEMail && !string.IsNullOrWhiteSpace(guardian.Emailadres);
    }

    internal static bool HasUsableName(OuderVerzorger guardian)
    {
        return !string.IsNullOrWhiteSpace(guardian.Voorletters)
            && !string.IsNullOrWhiteSpace(guardian.Achternaam);
    }

    internal static string GetGivenName(OuderVerzorger guardian)
    {
        return guardian.Voorletters?.Trim() ?? string.Empty;
    }

    internal static string GetFamilyName(OuderVerzorger guardian)
    {
        string prefix = guardian.Voorvoegsel?.Trim() ?? string.Empty;
        string surname = guardian.Achternaam?.Trim() ?? string.Empty;
        return prefix.Length == 0 ? surname : $"{prefix} {surname}";
    }

    internal static string GetPhone(OuderVerzorger guardian)
    {
        string normalizedFallback = BusinessLogicHelper.NormaliseerTelefoonnummerNaarE164(
            guardian.Telefoonnummer);

        PhoneCandidate[] candidates =
        [
            CreateCandidate(
                guardian.UitgebreidMobielNummer,
                guardian.Mobielwerknummer_geheim,
                normalizedFallback),
            CreateCandidate(
                guardian.UitgebreidTelefoonnummer,
                guardian.Telefoonnummer_geheim,
                normalizedFallback),
            CreateCandidate(
                guardian.UitgebreidMobielWerkNummer,
                guardian.Mobielwerknummer_geheim,
                normalizedFallback)
        ];

        foreach (PhoneCandidate candidate in candidates)
        {
            if (!candidate.IsSecret && candidate.NormalizedValue.Length > 0)
            {
                // A matched fallback is classified by this candidate's secret flag. An
                // unmatched fallback is never emitted directly; the explicit value wins.
                return candidate.MatchesFallback ? normalizedFallback : candidate.NormalizedValue;
            }
        }

        return string.Empty;
    }

    private static PhoneCandidate CreateCandidate(
        string value,
        bool isSecret,
        string normalizedFallback)
    {
        string normalizedValue = BusinessLogicHelper.NormaliseerTelefoonnummerNaarE164(value);
        return new PhoneCandidate(
            normalizedValue,
            isSecret,
            normalizedValue.Length > 0 && normalizedValue == normalizedFallback);
    }

    private sealed record PhoneCandidate(
        string NormalizedValue,
        bool IsSecret,
        bool MatchesFallback);
}
