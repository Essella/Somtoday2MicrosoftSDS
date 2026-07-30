namespace Somtoday2MicrosoftSDS.Helpers;

internal static class GuardianExportPolicy
{
    internal static bool IsExportable(OuderVerzorger guardian)
    {
        return guardian.WenstContactViaEMail && !string.IsNullOrWhiteSpace(guardian.Emailadres);
    }

    internal static string GetFamilyName(OuderVerzorger guardian)
    {
        return string.IsNullOrWhiteSpace(guardian.Voorvoegsel)
            ? guardian.Achternaam ?? string.Empty
            : $"{guardian.Voorvoegsel} {guardian.Achternaam}";
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
