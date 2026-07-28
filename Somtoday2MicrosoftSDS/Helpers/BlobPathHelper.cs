namespace Somtoday2MicrosoftSDS.Helpers
{
    internal static class BlobPathHelper
    {
        internal static string NormalizePrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                throw new ArgumentException("a non-empty blob prefix is required", nameof(prefix));
            }

            string[] segments = prefix
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            {
                throw new ArgumentException("the blob prefix contains an invalid path segment", nameof(prefix));
            }

            return string.Join('/', segments);
        }

        internal static string SanitizeSegment(string value, string segmentName)
        {
            string sanitized = value?.Trim().Replace('/', '_').Replace('\\', '_');
            if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
            {
                throw new ArgumentException($"{segmentName} is empty or invalid", segmentName);
            }

            return sanitized;
        }

        internal static string Combine(params string[] segments)
        {
            return string.Join('/', segments.Select(segment => segment.Trim('/')));
        }
    }
}
