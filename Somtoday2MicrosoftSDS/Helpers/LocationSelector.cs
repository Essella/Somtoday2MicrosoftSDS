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
                .Where(location => IsSelected(location.Afkorting, included, excluded))
                .ToList();
        }

        private static bool IsSelected(
            string locationCode,
            IReadOnlySet<string> included,
            IReadOnlySet<string> excluded)
        {
            string normalizedCode = locationCode?.Trim();
            if (included.Count > 0)
            {
                return !string.IsNullOrEmpty(normalizedCode) &&
                    included.Contains(normalizedCode) &&
                    !excluded.Contains(normalizedCode);
            }

            return string.IsNullOrEmpty(normalizedCode) || !excluded.Contains(normalizedCode);
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
