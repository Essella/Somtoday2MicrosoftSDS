using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Somtoday2MicrosoftSDS.Helpers
{
    internal enum DatasetPublicationResult
    {
        Succeeded,
        Failed
    }

    internal sealed class PublicationRollbackException : Exception
    {
        internal PublicationRollbackException(string message)
            : base(message)
        {
        }

        internal PublicationRollbackException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal sealed class DatasetPublisher
    {
        internal const string ProducerMetadataKey = "syncidproducer";
        internal const string RunUtcMetadataKey = "syncidrunutc";
        internal const string SdsVersionMetadataKey = "syncidsdsversion";
        internal const string GuardiansMetadataKey = "syncidguardians";
        internal const string ProducerMetadataValue = "Somtoday2MicrosoftSDS";

        private const int TotalPromotionAttempts = 4;
        private const string StagingSegment = "/.staging/";

        private readonly IBlobPublicationStore _store;
        private readonly ILogger<DatasetPublisher> _logger;
        private readonly DateTimeOffset _runStartedUtc;
        private readonly string _runId;

        internal DatasetPublisher(
            IBlobPublicationStore store,
            ILogger<DatasetPublisher> logger,
            DateTimeOffset runStartedUtc,
            string runId)
        {
            _store = store;
            _logger = logger;
            _runStartedUtc = runStartedUtc.ToUniversalTime();
            _runId = runId;
        }

        internal async Task CleanupStaleStagingAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _store.DeleteStaleStagingAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to remove staging data left by an earlier run ({Error})",
                    SafeExceptionSummary.Create(ex));
            }
        }

        internal async Task<DatasetPublicationResult> PublishAsync(
            PublicationDataset dataset,
            string livePrefix,
            CancellationToken cancellationToken)
        {
            string stagingPrefix = BlobPathHelper.Combine(livePrefix, ".staging", _runId);
            IReadOnlyDictionary<string, string> metadata = CreateMetadata(dataset);
            List<string> stagedBlobNames = dataset.Files
                .Select(file => BlobPathHelper.Combine(stagingPrefix, file.Name))
                .ToList();

            try
            {
                _logger.LogInformation(
                    "Staging {SdsVersion} dataset at {BlobPrefix}",
                    dataset.SdsVersion,
                    livePrefix);

                for (int index = 0; index < dataset.Files.Count; index++)
                {
                    await _store.UploadAsync(
                        stagedBlobNames[index],
                        dataset.Files[index].Content,
                        metadata,
                        cancellationToken);
                }

                for (int attempt = 1; attempt <= TotalPromotionAttempts; attempt++)
                {
                    try
                    {
                        _logger.LogInformation(
                            "Promoting {SdsVersion} dataset at {BlobPrefix} (attempt {Attempt}/{TotalAttempts})",
                            dataset.SdsVersion,
                            livePrefix,
                            attempt,
                            TotalPromotionAttempts);

                        if (!dataset.GuardianEnabled)
                        {
                            foreach (string guardianFileName in dataset.GuardianFileNames)
                            {
                                await _store.DeleteIfExistsAsync(
                                    BlobPathHelper.Combine(livePrefix, guardianFileName),
                                    cancellationToken);
                            }
                        }

                        for (int index = 0; index < dataset.Files.Count; index++)
                        {
                            await _store.CopyAsync(
                                stagedBlobNames[index],
                                BlobPathHelper.Combine(livePrefix, dataset.Files[index].Name),
                                metadata,
                                cancellationToken);
                        }

                        _logger.LogInformation(
                            "Published {SdsVersion} dataset at {BlobPrefix}",
                            dataset.SdsVersion,
                            livePrefix);
                        return DatasetPublicationResult.Succeeded;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (attempt < TotalPromotionAttempts)
                    {
                        _logger.LogWarning(
                            "Promotion failed for {SdsVersion} dataset at {BlobPrefix} (attempt {Attempt}/{TotalAttempts}, {Error})",
                            dataset.SdsVersion,
                            livePrefix,
                            attempt,
                            TotalPromotionAttempts,
                            SafeExceptionSummary.Create(ex));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            "Promotion exhausted for {SdsVersion} dataset at {BlobPrefix}; searching for a complete earlier app dataset ({Error})",
                            dataset.SdsVersion,
                            livePrefix,
                            SafeExceptionSummary.Create(ex));
                        await RollBackAsync(dataset, livePrefix, cancellationToken);
                        return DatasetPublicationResult.Failed;
                    }
                }

                throw new InvalidOperationException("The publication attempt loop ended unexpectedly");
            }
            finally
            {
                foreach (string stagedBlobName in stagedBlobNames)
                {
                    try
                    {
                        await _store.DeleteIfExistsAsync(stagedBlobName, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning(
                            "Failed to remove staging data for {SdsVersion} dataset at {BlobPrefix} ({Error})",
                            dataset.SdsVersion,
                            livePrefix,
                            SafeExceptionSummary.Create(ex));
                    }
                }
            }
        }

        internal static bool IsOwnedStagingBlob(
            string blobName,
            IReadOnlyDictionary<string, string> metadata)
        {
            return blobName.Contains(StagingSegment, StringComparison.Ordinal) &&
                TryGetMetadata(metadata, ProducerMetadataKey, out string producer) &&
                string.Equals(producer, ProducerMetadataValue, StringComparison.Ordinal);
        }

        private async Task RollBackAsync(
            PublicationDataset dataset,
            string livePrefix,
            CancellationToken cancellationToken)
        {
            RestoreSet restoreSet;
            try
            {
                restoreSet = await FindRestoreSetAsync(dataset, livePrefix, cancellationToken)
                    ?? throw new PublicationRollbackException(
                        $"No complete earlier {dataset.SdsVersion} application dataset is available for rollback");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (PublicationRollbackException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PublicationRollbackException(
                    $"Could not inspect Blob versions for {dataset.SdsVersion} rollback",
                    ex);
            }

            List<Exception> failures = [];
            foreach (string fileName in dataset.GetExpectedFileNames(restoreSet.GuardianEnabled))
            {
                try
                {
                    await _store.RestoreVersionAsync(
                        restoreSet.Files[fileName],
                        BlobPathHelper.Combine(livePrefix, fileName),
                        cancellationToken);
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

            if (!restoreSet.GuardianEnabled)
            {
                foreach (string guardianFileName in dataset.GuardianFileNames)
                {
                    try
                    {
                        await _store.DeleteIfExistsAsync(
                            BlobPathHelper.Combine(livePrefix, guardianFileName),
                            cancellationToken);
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
            }

            if (failures.Count > 0)
            {
                throw new PublicationRollbackException(
                    $"Rollback failed for one or more {dataset.SdsVersion} dataset files",
                    new AggregateException(failures));
            }

            _logger.LogWarning(
                "Restored {SdsVersion} dataset at {BlobPrefix} to complete app set {RestoreRunUtc}",
                dataset.SdsVersion,
                livePrefix,
                restoreSet.RunUtc);
        }

        private async Task<RestoreSet> FindRestoreSetAsync(
            PublicationDataset dataset,
            string livePrefix,
            CancellationToken cancellationToken)
        {
            Dictionary<(DateTimeOffset RunUtc, bool GuardianEnabled), List<BlobVersionItem>> groups = [];
            string prefix = livePrefix.TrimEnd('/') + "/";
            HashSet<string> knownNames = [.. dataset.CoreFileNames, .. dataset.GuardianFileNames];

            await foreach (BlobVersionItem item in _store.GetVersionsAsync(prefix, cancellationToken))
            {
                string fileName = item.Name[prefix.Length..];
                if (fileName.Contains('/') || !knownNames.Contains(fileName))
                {
                    continue;
                }

                if (!TryReadAppMetadata(
                    item.Metadata,
                    dataset.SdsVersion,
                    out DateTimeOffset runUtc,
                    out bool guardianEnabled) ||
                    runUtc >= _runStartedUtc)
                {
                    continue;
                }

                (DateTimeOffset RunUtc, bool GuardianEnabled) key = (runUtc, guardianEnabled);
                if (!groups.TryGetValue(key, out List<BlobVersionItem> items))
                {
                    items = [];
                    groups.Add(key, items);
                }

                items.Add(item);
            }

            foreach (((DateTimeOffset runUtc, bool guardianEnabled), List<BlobVersionItem> items) in
                groups.OrderByDescending(group => group.Key.RunUtc))
            {
                IReadOnlyList<string> expectedNames = dataset.GetExpectedFileNames(guardianEnabled);
                Dictionary<string, BlobVersionItem> selected = items
                    .GroupBy(item => item.Name[prefix.Length..], StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .OrderByDescending(item => item.LastModified)
                            .ThenByDescending(GetVersionTimestamp)
                            .First(),
                        StringComparer.OrdinalIgnoreCase);

                if (expectedNames.All(selected.ContainsKey))
                {
                    return new RestoreSet(runUtc, guardianEnabled, selected);
                }
            }

            return null;
        }

        private static DateTimeOffset GetVersionTimestamp(BlobVersionItem item)
        {
            return DateTimeOffset.TryParse(
                item.VersionId,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset timestamp)
                ? timestamp
                : DateTimeOffset.MinValue;
        }

        private IReadOnlyDictionary<string, string> CreateMetadata(PublicationDataset dataset)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ProducerMetadataKey] = ProducerMetadataValue,
                [RunUtcMetadataKey] = _runStartedUtc.ToString("O", CultureInfo.InvariantCulture),
                [SdsVersionMetadataKey] = dataset.SdsVersion,
                [GuardiansMetadataKey] = dataset.GuardianEnabled ? "true" : "false"
            };
        }

        private static bool TryReadAppMetadata(
            IReadOnlyDictionary<string, string> metadata,
            string expectedSdsVersion,
            out DateTimeOffset runUtc,
            out bool guardianEnabled)
        {
            runUtc = default;
            guardianEnabled = default;
            return TryGetMetadata(metadata, ProducerMetadataKey, out string producer) &&
                string.Equals(producer, ProducerMetadataValue, StringComparison.Ordinal) &&
                TryGetMetadata(metadata, RunUtcMetadataKey, out string runText) &&
                DateTimeOffset.TryParseExact(
                    runText,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out runUtc) &&
                TryGetMetadata(metadata, SdsVersionMetadataKey, out string sdsVersion) &&
                string.Equals(sdsVersion, expectedSdsVersion, StringComparison.Ordinal) &&
                TryGetMetadata(metadata, GuardiansMetadataKey, out string guardianText) &&
                bool.TryParse(guardianText, out guardianEnabled);
        }

        private static bool TryGetMetadata(
            IReadOnlyDictionary<string, string> metadata,
            string key,
            out string value)
        {
            foreach ((string candidateKey, string candidateValue) in metadata)
            {
                if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = candidateValue;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private sealed record RestoreSet(
            DateTimeOffset RunUtc,
            bool GuardianEnabled,
            IReadOnlyDictionary<string, BlobVersionItem> Files);
    }
}
