namespace Somtoday2MicrosoftSDS.Helpers
{
    internal sealed record PublicationFile(string Name, BinaryData Content);

    internal sealed class PublicationDataset
    {
        internal PublicationDataset(
            string sdsVersion,
            bool guardianEnabled,
            IReadOnlyList<PublicationFile> files,
            IReadOnlyList<string> coreFileNames,
            IReadOnlyList<string> guardianFileNames)
        {
            SdsVersion = sdsVersion;
            GuardianEnabled = guardianEnabled;
            Files = files;
            CoreFileNames = coreFileNames;
            GuardianFileNames = guardianFileNames;
        }

        internal string SdsVersion { get; }

        internal bool GuardianEnabled { get; }

        internal IReadOnlyList<PublicationFile> Files { get; }

        internal IReadOnlyList<string> CoreFileNames { get; }

        internal IReadOnlyList<string> GuardianFileNames { get; }

        internal IReadOnlyList<string> GetExpectedFileNames(bool guardianEnabled)
        {
            return guardianEnabled
                ? [.. CoreFileNames, .. GuardianFileNames]
                : CoreFileNames;
        }
    }
}
