using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;

namespace Somtoday2MicrosoftSDS.Helpers;

internal readonly record struct StoredSecret(bool Found, string Value)
{
    internal static StoredSecret Missing => new(false, null);
}

internal interface ISecretStore
{
    Task<StoredSecret> GetAsync(string name, CancellationToken cancellationToken);

    Task SetAsync(string name, string value, CancellationToken cancellationToken);
}

internal sealed class KeyVaultSecretStore : ISecretStore
{
    private readonly SecretClient client;

    internal KeyVaultSecretStore(Uri vaultUri)
    {
        client = new SecretClient(vaultUri, new DefaultAzureCredential());
    }

    public async Task<StoredSecret> GetAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            Response<KeyVaultSecret> response = await client.GetSecretAsync(name, cancellationToken: cancellationToken);
            return new StoredSecret(true, response.Value.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return StoredSecret.Missing;
        }
    }

    public async Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        await client.SetSecretAsync(new KeyVaultSecret(name, value), cancellationToken);
    }
}

internal sealed class SomtodaySecretProvider
{
    internal const string DefaultSecretName = "somtoday-client-secret";
    internal const string BootstrapEnvironmentVariable = "Somtoday__ClientSecret";

    private readonly ILogger<SomtodaySecretProvider> logger;
    private readonly Func<Uri, ISecretStore> storeFactory;

    public SomtodaySecretProvider(ILogger<SomtodaySecretProvider> logger)
        : this(logger, vaultUri => new KeyVaultSecretStore(vaultUri))
    {
    }

    internal SomtodaySecretProvider(
        ILogger<SomtodaySecretProvider> logger,
        Func<Uri, ISecretStore> storeFactory)
    {
        this.logger = logger;
        this.storeFactory = storeFactory;
    }

    internal async Task<string> ResolveAsync(
        string configuredVaultUri,
        string configuredSecretName,
        string bootstrapSecret,
        string developmentSecret,
        bool isDevelopment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuredVaultUri))
        {
            if (!isDevelopment)
            {
                throw new InvalidOperationException("KeyVault:VaultUri is required outside Development");
            }

            if (string.IsNullOrWhiteSpace(developmentSecret))
            {
                throw new InvalidOperationException(
                    "Somtoday:ClientSecret is required from an environment variable or .NET User Secrets when Key Vault is disabled in Development");
            }

            logger.LogInformation("Development mode uses the directly configured Somtoday client secret");
            return developmentSecret;
        }

        if (!Uri.TryCreate(configuredVaultUri.Trim(), UriKind.Absolute, out Uri vaultUri)
            || vaultUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("KeyVault:VaultUri must be an absolute HTTPS URI");
        }

        string secretName = string.IsNullOrWhiteSpace(configuredSecretName)
            ? DefaultSecretName
            : configuredSecretName.Trim();
        ValidateSecretName(secretName);

        ISecretStore store = storeFactory(vaultUri);
        StoredSecret storedSecret = await store.GetAsync(secretName, cancellationToken);

        if (!storedSecret.Found)
        {
            if (string.IsNullOrWhiteSpace(bootstrapSecret))
            {
                throw new InvalidOperationException(
                    $"Key Vault secret '{secretName}' does not exist and {BootstrapEnvironmentVariable} was not provided");
            }

            await store.SetAsync(secretName, bootstrapSecret, cancellationToken);
            logger.LogInformation(
                "Secret in Vault opgeslagen. Verwijder {EnvironmentVariable} uit de environment-configuratie.",
                BootstrapEnvironmentVariable);
            return bootstrapSecret;
        }

        if (string.IsNullOrWhiteSpace(bootstrapSecret))
        {
            return storedSecret.Value;
        }

        if (SecretsEqual(storedSecret.Value, bootstrapSecret))
        {
            logger.LogWarning(
                "Secret komt overeen met Vault. Verwijder {EnvironmentVariable} uit de environment-configuratie.",
                BootstrapEnvironmentVariable);
            return storedSecret.Value;
        }

        await store.SetAsync(secretName, bootstrapSecret, cancellationToken);
        logger.LogInformation("Secret in Vault bijgewerkt.");
        return bootstrapSecret;
    }

    private static void ValidateSecretName(string secretName)
    {
        if (secretName.Length is < 1 or > 127
            || secretName.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new InvalidOperationException(
                "KeyVault:SomtodayClientSecretName may contain only letters, digits, and hyphens");
        }
    }

    private static bool SecretsEqual(string left, string right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);

        try
        {
            return leftBytes.Length == rightBytes.Length
                && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }
}
