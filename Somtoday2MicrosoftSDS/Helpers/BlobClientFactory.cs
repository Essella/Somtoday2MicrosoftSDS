using Azure.Identity;
using Azure.Core;
using Azure.Storage.Blobs;

namespace Somtoday2MicrosoftSDS.Helpers
{
    internal enum BlobAuthenticationMode
    {
        DefaultAzureCredential,
        ConnectionString
    }

    internal static class BlobClientFactory
    {
        internal static BlobAuthenticationMode GetAuthenticationMode(SyncConfiguration configuration)
        {
            return string.IsNullOrWhiteSpace(configuration.BlobServiceUri)
                ? BlobAuthenticationMode.ConnectionString
                : BlobAuthenticationMode.DefaultAzureCredential;
        }

        internal static BlobStorageContext CreateStorageContext(SyncConfiguration configuration)
        {
            if (GetAuthenticationMode(configuration) == BlobAuthenticationMode.DefaultAzureCredential)
            {
                DefaultAzureCredential credential = new DefaultAzureCredential();
                BlobServiceClient serviceClient = new BlobServiceClient(
                    new Uri(configuration.BlobServiceUri),
                    credential);
                return new BlobStorageContext(
                    serviceClient.GetBlobContainerClient(configuration.BlobContainer),
                    credential);
            }

            return new BlobStorageContext(
                new BlobContainerClient(configuration.BlobConnectionString, configuration.BlobContainer),
                TokenCredential: null);
        }

        internal static BlobContainerClient CreateContainerClient(SyncConfiguration configuration)
        {
            return CreateStorageContext(configuration).ContainerClient;
        }
    }
}
