using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;

namespace Somtoday2MicrosoftSDS.Helpers
{
    internal sealed record BlobStorageContext(
        BlobContainerClient ContainerClient,
        TokenCredential TokenCredential);

    internal sealed record BlobRestoreSource(
        string Name,
        string VersionId,
        DateTimeOffset LastModified,
        IReadOnlyDictionary<string, string> Metadata);

    internal interface IBlobPublicationStore
    {
        Task EnsureContainerExistsAsync(CancellationToken cancellationToken);

        Task UploadAsync(
            string blobName,
            BinaryData content,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken);

        Task CopyAsync(
            string sourceBlobName,
            string destinationBlobName,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken);

        Task RestoreAsync(
            BlobRestoreSource source,
            string destinationBlobName,
            CancellationToken cancellationToken);

        Task DeleteIfExistsAsync(string blobName, CancellationToken cancellationToken);

        IAsyncEnumerable<BlobRestoreSource> GetRestoreSourcesAsync(
            string prefix,
            CancellationToken cancellationToken);

        Task DeleteStaleStagingAsync(CancellationToken cancellationToken);
    }

    internal sealed class AzureBlobPublicationStore : IBlobPublicationStore
    {
        private static readonly TokenRequestContext StorageTokenRequest = new(
            ["https://storage.azure.com/.default"]);

        private readonly BlobContainerClient _containerClient;
        private readonly TokenCredential _tokenCredential;

        internal AzureBlobPublicationStore(BlobStorageContext context)
        {
            _containerClient = context.ContainerClient;
            _tokenCredential = context.TokenCredential;
        }

        public async Task EnsureContainerExistsAsync(CancellationToken cancellationToken)
        {
            await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        public async Task UploadAsync(
            string blobName,
            BinaryData content,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken)
        {
            using Stream stream = content.ToStream();
            await _containerClient.GetBlobClient(blobName).UploadAsync(
                stream,
                new BlobUploadOptions
                {
                    Metadata = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
                },
                cancellationToken);
        }

        public async Task CopyAsync(
            string sourceBlobName,
            string destinationBlobName,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken)
        {
            BlobClient source = _containerClient.GetBlobClient(sourceBlobName);
            await CopyFromSourceAsync(source, destinationBlobName, metadata, cancellationToken);
        }

        public async Task RestoreAsync(
            BlobRestoreSource source,
            string destinationBlobName,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(source.VersionId))
            {
                if (string.Equals(source.Name, destinationBlobName, StringComparison.Ordinal))
                {
                    return;
                }

                throw new InvalidOperationException(
                    "A versionless Blob restore source must already be the destination Blob");
            }

            BlobClient sourceBlob = _containerClient
                .GetBlobClient(source.Name)
                .WithVersion(source.VersionId);
            await CopyFromSourceAsync(
                sourceBlob,
                destinationBlobName,
                source.Metadata,
                cancellationToken);
        }

        public async Task DeleteIfExistsAsync(string blobName, CancellationToken cancellationToken)
        {
            await _containerClient.GetBlobClient(blobName).DeleteIfExistsAsync(
                cancellationToken: cancellationToken);
        }

        public async IAsyncEnumerable<BlobRestoreSource> GetRestoreSourcesAsync(
            string prefix,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (BlobItem item in _containerClient.GetBlobsAsync(
                BlobTraits.Metadata,
                BlobStates.Version,
                prefix,
                cancellationToken))
            {
                if (item.Properties.LastModified is null)
                {
                    continue;
                }

                yield return new BlobRestoreSource(
                    item.Name,
                    item.VersionId,
                    item.Properties.LastModified.Value,
                    new Dictionary<string, string>(item.Metadata, StringComparer.OrdinalIgnoreCase));
            }
        }

        public async Task DeleteStaleStagingAsync(CancellationToken cancellationToken)
        {
            List<string> staleBlobs = [];
            List<Exception> failures = [];
            try
            {
                await foreach (BlobItem item in _containerClient.GetBlobsAsync(
                    BlobTraits.Metadata,
                    BlobStates.None,
                    cancellationToken: cancellationToken))
                {
                    if (DatasetPublisher.IsOwnedStagingBlob(
                        item.Name,
                        new Dictionary<string, string>(item.Metadata, StringComparer.OrdinalIgnoreCase)))
                    {
                        staleBlobs.Add(item.Name);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }

            foreach (string blobName in staleBlobs)
            {
                try
                {
                    await DeleteIfExistsAsync(blobName, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(failures);
            }
        }

        private async Task CopyFromSourceAsync(
            BlobClient source,
            string destinationBlobName,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken)
        {
            (Uri sourceUri, HttpAuthorization sourceAuthentication) =
                await GetAuthorizedSourceAsync(source, cancellationToken);
            BlockBlobClient destination = _containerClient.GetBlockBlobClient(destinationBlobName);
            await destination.SyncUploadFromUriAsync(
                sourceUri,
                new BlobSyncUploadFromUriOptions
                {
                    CopySourceBlobProperties = true,
                    Metadata = new Dictionary<string, string>(metadata, StringComparer.Ordinal),
                    SourceAuthentication = sourceAuthentication
                },
                cancellationToken);
        }

        private async Task<(Uri SourceUri, HttpAuthorization SourceAuthentication)> GetAuthorizedSourceAsync(
            BlobClient source,
            CancellationToken cancellationToken)
        {
            if (_tokenCredential is not null)
            {
                AccessToken token = await _tokenCredential.GetTokenAsync(
                    StorageTokenRequest,
                    cancellationToken);
                return (source.Uri, new HttpAuthorization("Bearer", token.Token));
            }

            if (!source.CanGenerateSasUri)
            {
                throw new InvalidOperationException(
                    "The Blob copy source cannot be authorized with the configured storage client");
            }

            Uri sourceUri = source.GenerateSasUri(
                BlobSasPermissions.Read,
                DateTimeOffset.UtcNow.AddMinutes(15));
            return (sourceUri, null);
        }
    }
}
