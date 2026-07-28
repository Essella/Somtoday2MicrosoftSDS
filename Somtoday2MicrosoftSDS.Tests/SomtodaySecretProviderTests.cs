using Azure;
using Microsoft.Extensions.Logging;
using Somtoday2MicrosoftSDS.Helpers;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public class SomtodaySecretProviderTests
{
    private static readonly string VaultUri = "https://example.vault.azure.net/";

    [Fact]
    public async Task MissingVaultSecretIsStoredFromBootstrapEnvironmentValue()
    {
        FakeSecretStore store = new(StoredSecret.Missing);
        CapturingLogger<SomtodaySecretProvider> logger = new();
        SomtodaySecretProvider provider = CreateProvider(store, logger);

        string resolved = await provider.ResolveAsync(
            VaultUri,
            SomtodaySecretProvider.DefaultSecretName,
            "new-secret",
            null,
            isDevelopment: false,
            CancellationToken.None);

        Assert.Equal("new-secret", resolved);
        Assert.Equal(1, store.SetCount);
        Assert.Equal("new-secret", store.Current.Value);
        Assert.Contains(logger.Messages, message => message.Contains("Secret in Vault opgeslagen", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("new-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MatchingBootstrapSecretRequestsEnvironmentRemovalWithoutWriting()
    {
        FakeSecretStore store = new(new StoredSecret(true, "same-secret"));
        CapturingLogger<SomtodaySecretProvider> logger = new();
        SomtodaySecretProvider provider = CreateProvider(store, logger);

        string resolved = await provider.ResolveAsync(
            VaultUri,
            null,
            "same-secret",
            null,
            isDevelopment: false,
            CancellationToken.None);

        Assert.Equal("same-secret", resolved);
        Assert.Equal(0, store.SetCount);
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                "Secret komt overeen met Vault. Verwijder Somtoday__ClientSecret uit de environment-configuratie.",
                StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("same-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DifferentBootstrapSecretCreatesNewVaultVersion()
    {
        FakeSecretStore store = new(new StoredSecret(true, "old-secret"));
        CapturingLogger<SomtodaySecretProvider> logger = new();
        SomtodaySecretProvider provider = CreateProvider(store, logger);

        string resolved = await provider.ResolveAsync(
            VaultUri,
            null,
            "new-secret",
            null,
            isDevelopment: false,
            CancellationToken.None);

        Assert.Equal("new-secret", resolved);
        Assert.Equal(1, store.SetCount);
        Assert.Equal("new-secret", store.Current.Value);
        Assert.Contains(logger.Messages, message => message == "Secret in Vault bijgewerkt.");
        Assert.DoesNotContain(logger.Messages, message =>
            message.Contains("old-secret", StringComparison.Ordinal)
            || message.Contains("new-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExistingVaultSecretIsUsedWithoutBootstrapValue()
    {
        FakeSecretStore store = new(new StoredSecret(true, "vault-secret"));
        SomtodaySecretProvider provider = CreateProvider(store, new CapturingLogger<SomtodaySecretProvider>());

        string resolved = await provider.ResolveAsync(
            VaultUri,
            null,
            null,
            null,
            isDevelopment: false,
            CancellationToken.None);

        Assert.Equal("vault-secret", resolved);
        Assert.Equal(0, store.SetCount);
    }

    [Fact]
    public async Task MissingVaultSecretWithoutBootstrapValueFails()
    {
        SomtodaySecretProvider provider = CreateProvider(
            new FakeSecretStore(StoredSecret.Missing),
            new CapturingLogger<SomtodaySecretProvider>());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ResolveAsync(
                VaultUri,
                null,
                null,
                null,
                isDevelopment: false,
                CancellationToken.None));

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnexpectedVaultFailureIsNotBypassedByBootstrapValue()
    {
        InvalidOperationException vaultFailure = new("vault unavailable");
        FakeSecretStore store = new(StoredSecret.Missing) { GetException = vaultFailure };
        SomtodaySecretProvider provider = CreateProvider(store, new CapturingLogger<SomtodaySecretProvider>());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ResolveAsync(
                VaultUri,
                null,
                "bootstrap-secret",
                null,
                isDevelopment: false,
                CancellationToken.None));

        Assert.Same(vaultFailure, exception);
        Assert.Equal(0, store.SetCount);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(500)]
    public async Task VaultAuthorizationAndServiceFailuresStopTheRun(int status)
    {
        RequestFailedException vaultFailure = new(status, "response-must-not-be-used");
        FakeSecretStore store = new(StoredSecret.Missing) { GetException = vaultFailure };
        SomtodaySecretProvider provider = CreateProvider(store, new CapturingLogger<SomtodaySecretProvider>());

        RequestFailedException exception = await Assert.ThrowsAsync<RequestFailedException>(() =>
            provider.ResolveAsync(
                VaultUri,
                null,
                "bootstrap-secret",
                null,
                isDevelopment: false,
                CancellationToken.None));

        Assert.Same(vaultFailure, exception);
        Assert.Equal(0, store.SetCount);
    }

    [Fact]
    public async Task FailedVaultUpdateStopsTheRunAndDoesNotReturnBootstrapSecret()
    {
        InvalidOperationException updateFailure = new("update failed");
        FakeSecretStore store = new(new StoredSecret(true, "old-secret"))
        {
            SetException = updateFailure
        };
        SomtodaySecretProvider provider = CreateProvider(store, new CapturingLogger<SomtodaySecretProvider>());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ResolveAsync(
                VaultUri,
                null,
                "new-secret",
                null,
                isDevelopment: false,
                CancellationToken.None));

        Assert.Same(updateFailure, exception);
        Assert.Equal(1, store.SetCount);
        Assert.Equal("old-secret", store.Current.Value);
    }

    [Fact]
    public async Task DevelopmentCanUseDirectSecretWithoutVault()
    {
        SomtodaySecretProvider provider = CreateProvider(
            new FakeSecretStore(StoredSecret.Missing),
            new CapturingLogger<SomtodaySecretProvider>());

        string resolved = await provider.ResolveAsync(
            null,
            null,
            null,
            "development-secret",
            isDevelopment: true,
            CancellationToken.None);

        Assert.Equal("development-secret", resolved);
    }

    [Fact]
    public async Task ProductionRequiresVaultUri()
    {
        SomtodaySecretProvider provider = CreateProvider(
            new FakeSecretStore(StoredSecret.Missing),
            new CapturingLogger<SomtodaySecretProvider>());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ResolveAsync(
                null,
                null,
                "bootstrap-secret",
                null,
                isDevelopment: false,
                CancellationToken.None));

        Assert.Contains("VaultUri is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        SomtodaySecretProvider provider = CreateProvider(
            new FakeSecretStore(StoredSecret.Missing),
            new CapturingLogger<SomtodaySecretProvider>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.ResolveAsync(
                VaultUri,
                null,
                "bootstrap-secret",
                null,
                isDevelopment: false,
                cancellation.Token));
    }

    private static SomtodaySecretProvider CreateProvider(
        FakeSecretStore store,
        CapturingLogger<SomtodaySecretProvider> logger)
    {
        return new SomtodaySecretProvider(logger, _ => store);
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        internal FakeSecretStore(StoredSecret current)
        {
            Current = current;
        }

        internal StoredSecret Current { get; private set; }

        internal int SetCount { get; private set; }

        internal Exception GetException { get; init; }

        internal Exception SetException { get; init; }

        public Task<StoredSecret> GetAsync(string name, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetException is not null)
            {
                throw GetException;
            }

            return Task.FromResult(Current);
        }

        public Task SetAsync(string name, string value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetCount++;
            if (SetException is not null)
            {
                throw SetException;
            }

            Current = new StoredSecret(true, value);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        internal List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        internal static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
