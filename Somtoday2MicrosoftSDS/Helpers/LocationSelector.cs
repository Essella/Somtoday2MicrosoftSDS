namespace Somtoday2MicrosoftSDS.Helpers
{
    internal static class LocationSelector
    {
        internal static List<Vestiging> Select(
            IEnumerable<Vestiging> locations,
            IEnumerable<string> includedLocationCodes,
            IEnumerable<string> excludedLocationCodes)
        {
            HashSet<string> included = NormalizeCodes(includedLocationCodes);
            HashSet<string> excluded = NormalizeCodes(excludedLocationCodes);

            return locations
                .Where(location => !string.IsNullOrWhiteSpace(location.Afkorting))
                .Where(location => included.Count == 0 || included.Contains(location.Afkorting.Trim()))
                .Where(location => !excluded.Contains(location.Afkorting.Trim()))
                .ToList();
        }

        private static HashSet<string> NormalizeCodes(IEnumerable<string> codes)
        {
            return codes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
