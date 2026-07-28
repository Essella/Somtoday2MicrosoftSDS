using Azure.Identity;
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

        internal static BlobContainerClient CreateContainerClient(SyncConfiguration configuration)
        {
            if (GetAuthenticationMode(configuration) == BlobAuthenticationMode.DefaultAzureCredential)
            {
                BlobServiceClient serviceClient = new BlobServiceClient(
                    new Uri(configuration.BlobServiceUri),
                    new DefaultAzureCredential());
                return serviceClient.GetBlobContainerClient(configuration.BlobContainer);
            }

            return new BlobContainerClient(configuration.BlobConnectionString, configuration.BlobContainer);
        }
    }
}
