namespace Somtoday2MicrosoftSDS.Helpers;

internal enum SdsDatasetFormat
{
    V1,
    V2Rev1
}

internal sealed record PublicationFile(string Name, BinaryData Content);

internal sealed record PublicationDataset(
    SdsDatasetFormat Format,
    IReadOnlyList<PublicationFile> Files);
