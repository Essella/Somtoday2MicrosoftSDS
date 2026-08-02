using Microsoft.Extensions.Logging;
using Somtoday2MicrosoftSDS.Helpers;
using Somtoday2MicrosoftSDS.Models;
using Xunit;

namespace Somtoday2MicrosoftSDS.Tests;

public sealed class DatasetPublisherTests
{
    private const string OutputPrefix = "output";
    private const string RunId = "0198d4e8fe8c70008000000000000001";
    private static readonly DateTimeOffset RunUtc = new(2026, 8, 1, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task StagesCompleteSetBeforePromotionAndCopiesRunMetadata()
    {
        VersionAwareBlobStore store = new();
        PublicationDataset dataset = CreateV2Dataset(guardians: true);
        DatasetPublisher publisher = CreatePublisher(store);

        DatasetPublicationResult result = await publisher.PublishAsync(
            dataset,
            "output/v2",
            CancellationToken.None);

        Assert.Equal(DatasetPublicationResult.Succeeded, result);
        Assert.Equal(0, store.GetVersionsCalls);
        Assert.Contains($"upload:{OutputPrefix}/.staging/{RunId}/orgs.csv", store.Operations);
        int firstCopy = store.Operations.FindIndex(operation => operation.StartsWith("copy:", StringComparison.Ordinal));
        Assert.Equal(dataset.Files.Count, store.Operations.Take(firstCopy).Count(
            operation => operation.StartsWith("upload:", StringComparison.Ordinal)));

        Assert.All(store.Uploads, upload =>
        {
            Assert.StartsWith($"{OutputPrefix}/.staging/{RunId}/", upload.Name, StringComparison.Ordinal);
            Assert.Equal(4, upload.Metadata.Count);
            Assert.Equal(DatasetPublisher.ProducerMetadataValue, upload.Metadata[DatasetPublisher.ProducerMetadataKey]);
            Assert.Equal(RunUtc.ToString("O"), upload.Metadata[DatasetPublisher.RunUtcMetadataKey]);
            Assert.Equal("v2", upload.Metadata[DatasetPublisher.SdsVersionMetadataKey]);
            Assert.Equal("true", upload.Metadata[DatasetPublisher.GuardiansMetadataKey]);
        });

        foreach (PublicationFile file in dataset.Files)
        {
            VersionAwareBlobStore.StoredBlob live = store.Current["output/v2/" + file.Name];
            Assert.Equal(4, live.Metadata.Count);
            Assert.Equal(DatasetPublisher.ProducerMetadataValue, live.Metadata[DatasetPublisher.ProducerMetadataKey]);
            Assert.Equal(RunUtc.ToString("O"), live.Metadata[DatasetPublisher.RunUtcMetadataKey]);
            Assert.Equal("v2", live.Metadata[DatasetPublisher.SdsVersionMetadataKey]);
            Assert.Equal("true", live.Metadata[DatasetPublisher.GuardiansMetadataKey]);
        }

        Assert.DoesNotContain(store.Current.Keys, name => name.Contains("/.staging/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReusedStagingNameIsOverwrittenBeforePromotingTheNextSequentialScope()
    {
        bool failCleanup = true;
        VersionAwareBlobStore store = new()
        {
            ShouldFailDelete = (_, name) =>
                failCleanup && name.Contains("/.staging/", StringComparison.Ordinal)
        };
        DatasetPublisher publisher = CreatePublisher(store);
        PublicationDataset firstDataset = CreateCompleteV2Dataset("first scope");
        PublicationDataset secondDataset = CreateCompleteV2Dataset("second scope");

        DatasetPublicationResult firstResult = await publisher.PublishAsync(
            firstDataset,
            "output/FIRST/v2",
            CancellationToken.None);

        Assert.Equal(DatasetPublicationResult.Succeeded, firstResult);
        Assert.All(firstDataset.Files, file => Assert.Equal(
            $"first scope:{file.Name}",
            store.Current[$"{OutputPrefix}/.staging/{RunId}/{file.Name}"].Content.ToString()));

        failCleanup = false;
        DatasetPublicationResult secondResult = await publisher.PublishAsync(
            secondDataset,
            "output/SECOND/v2",
            CancellationToken.None);

        Assert.Equal(DatasetPublicationResult.Succeeded, secondResult);
        Assert.All(secondDataset.Files, file =>
        {
            string stagingName = $"{OutputPrefix}/.staging/{RunId}/{file.Name}";
            Assert.Equal(
                $"first scope:{file.Name}",
                store.Current[$"output/FIRST/v2/{file.Name}"].Content.ToString());
            Assert.Equal(
                $"second scope:{file.Name}",
                store.Current[$"output/SECOND/v2/{file.Name}"].Content.ToString());
            Assert.Equal(2, store.Operations.Count(operation => operation == "upload:" + stagingName));
            Assert.True(
                store.Operations.LastIndexOf("upload:" + stagingName) <
                store.Operations.IndexOf($"copy:output/SECOND/v2/{file.Name}"));
            Assert.DoesNotContain(stagingName, store.Current.Keys);
        });
    }

    [Fact]
    public async Task RetriesTheCompletePromotionThreeTimesAfterTheFirstAttempt()
    {
        VersionAwareBlobStore store = new()
        {
            ShouldFailCopy = (copyNumber, _) => copyNumber <= 3
        };
        store.AddCurrent(
            "output/v2/relationships.csv",
            "old guardian",
            Metadata(RunUtc.AddHours(-1), "v2", guardians: true));
        DatasetPublisher publisher = CreatePublisher(store);

        DatasetPublicationResult result = await publisher.PublishAsync(
            CreateV2Dataset(guardians: false),
            "output/v2",
            CancellationToken.None);

        Assert.Equal(DatasetPublicationResult.Succeeded, result);
        Assert.Equal(8, store.CopyAttempts.Count);
        Assert.Equal(4, store.CopyAttempts.Count(name => name == "output/v2/orgs.csv"));
        Assert.True(
            store.Operations.IndexOf("delete:output/v2/relationships.csv") <
            store.Operations.FindIndex(operation => operation.StartsWith("copy:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RollbackSelectsNewestCompleteOlderAppSetAndRestoresGuardianState()
    {
        VersionAwareBlobStore store = new()
        {
            ShouldFailCopy = (_, destination) => destination.EndsWith("users.csv", StringComparison.Ordinal)
        };
        PublicationDataset dataset = CreateV2Dataset(guardians: true);
        DateTimeOffset restoreRun = RunUtc.AddHours(-3);
        IReadOnlyDictionary<string, string> restoreMetadata = Metadata(restoreRun, "v2", guardians: false);

        foreach (string fileName in dataset.CoreFileNames)
        {
            store.AddVersion(
                "output/v2/" + fileName,
                fileName == "orgs.csv"
                    ? "2026-08-01T07:31:00.0000000Z"
                    : "restore-" + fileName,
                restoreMetadata,
                restoreRun.AddMinutes(1),
                "old-" + fileName);
        }

        store.AddVersion(
            "output/v2/orgs.csv",
            "2026-08-01T07:32:00.0000000Z",
            restoreMetadata,
            restoreRun.AddMinutes(1),
            "latest-orgs");

        DateTimeOffset incompleteRun = RunUtc.AddHours(-2);
        foreach (string fileName in dataset.CoreFileNames.Take(dataset.CoreFileNames.Count - 1))
        {
            store.AddVersion(
                "output/v2/" + fileName,
                "incomplete-" + fileName,
                Metadata(incompleteRun, "v2", guardians: false),
                incompleteRun,
                "incomplete");
        }

        DateTimeOffset metadataLessRun = RunUtc.AddHours(-1);
        foreach (string fileName in dataset.CoreFileNames)
        {
            store.AddVersion(
                "output/v2/" + fileName,
                "manual-" + fileName,
                new Dictionary<string, string>(),
                metadataLessRun,
                "manual");
        }

        store.AddCurrent("output/v2/relationships.csv", "current guardian", Metadata(RunUtc.AddDays(-1), "v2", true));
        store.AddCurrent("output/v2/manual.csv", "manual", new Dictionary<string, string>());

        DatasetPublicationResult result = await CreatePublisher(store).PublishAsync(
            dataset,
            "output/v2",
            CancellationToken.None);

        Assert.Equal(DatasetPublicationResult.Failed, result);
        Assert.Contains("2026-08-01T07:32:00.0000000Z", store.RestoredVersionIds);
        Assert.Equal(dataset.CoreFileNames.Count, store.RestoreAttempts.Count);
        Assert.DoesNotContain("output/v2/relationships.csv", store.Current.Keys);
        Assert.Contains("output/v2/manual.csv", store.Current.Keys);
        Assert.DoesNotContain(store.Current.Keys, name => name.Contains("/.staging/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingCompleteRollbackSourceStopsFatallyWithoutRestore()
    {
        VersionAwareBlobStore store = new()
        {
            ShouldFailCopy = (_, destination) => destination.EndsWith("orgs.csv", StringComparison.Ordinal)
        };
        PublicationDataset dataset = CreateV2Dataset(guardians: false);
        store.AddVersion(
            "output/v2/orgs.csv",
            "partial",
            Metadata(RunUtc.AddHours(-1), "v2", guardians: false),
            RunUtc.AddHours(-1),
            "partial");

        await Assert.ThrowsAsync<PublicationRollbackException>(() => CreatePublisher(store).PublishAsync(
            dataset,
            "output/v2",
            CancellationToken.None));

        Assert.Empty(store.RestoreAttempts);
    }

    [Fact]
    public async Task RollbackAcceptsCompleteOlderSetFromVersionlessBaseBlobs()
    {
        VersionAwareBlobStore store = new()
        {
            ShouldFailCopy = (_, destination) => destination.EndsWith("orgs.csv", StringComparison.Ordinal)
        };
        PublicationDataset dataset = CreateV2Dataset(guardians: false);
        DateTimeOffset restoreRun = RunUtc.AddHours(-1);
        foreach (string fileName in dataset.CoreFileNames)
        {
            store.AddCurrent(
                "output/v2/" + fileName,
                "old-" + fileName,
                Metadata(restoreRun, "v2", guardians: false));
        }

        DatasetPublicationResult result = await CreatePublisher(store).PublishAsync(
            dataset,
            "output/v2",
            CancellationToken.None);

        Assert.Equal(DatasetPublicationResult.Failed, result);
        Assert.Equal(dataset.CoreFileNames.Count, store.RestoreAttempts.Count);
        Assert.All(
            dataset.CoreFileNames,
            fileName => Assert.Equal(
                "old-" + fileName,
                store.Current["output/v2/" + fileName].Content.ToString()));
        Assert.Empty(store.RestoredVersionIds);
    }

    [Fact]
    public async Task RollbackCombinesVersionedAndVersionlessSourcesAfterPartialPromotion()
    {
        VersionAwareBlobStore store = new()
        {
            ShouldFailCopy = (_, destination) => destination.EndsWith("users.csv", StringComparison.Ordinal)
        };
        PublicationDataset dataset = CreateV2Dataset(guardians: false);
        DateTimeOffset restoreRun = RunUtc.AddHours(-1);
        foreach (string fileName in dataset.CoreFileNames)
        {
            store.AddCurrent(
                "output/v2/" + fileName,
                "old-" + fileName,
                Metadata(restoreRun, "v2", guardians: false));
        }

        DatasetPublicationResult result = await CreatePublisher(store).PublishAsync(
            dataset,
            "output/v2",
            CancellationToken.None);

        Assert.Equal(DatasetPublicationResult.Failed, result);
        Assert.Equal(dataset.CoreFileNames.Count, store.RestoreAttempts.Count);
        Assert.Single(store.RestoredVersionIds);
        Assert.All(
            dataset.CoreFileNames,
            fileName => Assert.Equal(
                "old-" + fileName,
                store.Current["output/v2/" + fileName].Content.ToString()));
    }

    [Fact]
    public async Task V1GuardianRollbackRequiresBothGuardianFiles()
    {
        VersionAwareBlobStore store = new()
        {
            ShouldFailCopy = (_, destination) => destination.EndsWith("School.csv", StringComparison.Ordinal)
        };
        PublicationDataset dataset = new FileHelper().CreateEmptyV1Dataset(includeGuardianSync: true);
        DateTimeOffset restoreRun = RunUtc.AddHours(-1);
        IReadOnlyDictionary<string, string> metadata = Metadata(restoreRun, "v1", guardians: true);

        foreach (string fileName in dataset.CoreFileNames.Concat(["User.csv"]))
        {
            store.AddVersion(
                "output/v1/" + fileName,
                "partial-" + fileName,
                metadata,
                restoreRun,
                "partial");
        }

        await Assert.ThrowsAsync<PublicationRollbackException>(() => CreatePublisher(store).PublishAsync(
            dataset,
            "output/v1",
            CancellationToken.None));

        Assert.Empty(store.RestoreAttempts);
    }

    [Fact]
    public async Task RollbackNeverUsesCompleteLegacyStagingAsARestoreSource()
    {
        VersionAwareBlobStore store = new()
        {
            ShouldFailCopy = (_, destination) => destination.EndsWith("orgs.csv", StringComparison.Ordinal)
        };
        PublicationDataset dataset = CreateV2Dataset(guardians: false);
        DateTimeOffset restoreRun = RunUtc.AddHours(-1);

        foreach (string fileName in dataset.CoreFileNames)
        {
            store.AddVersion(
                $"output/v2/.staging/{RunId}/{fileName}",
                "staged-" + fileName,
                Metadata(restoreRun, "v2", guardians: false),
                restoreRun,
                "staged content");
        }

        await Assert.ThrowsAsync<PublicationRollbackException>(() => CreatePublisher(store).PublishAsync(
            dataset,
            "output/v2",
            CancellationToken.None));

        Assert.Equal(1, store.GetVersionsCalls);
        Assert.Empty(store.RestoreAttempts);
    }

    [Fact]
    public async Task RollbackAttemptsRemainingFilesBeforeReportingFatalFailure()
    {
        VersionAwareBlobStore store = new()
        {
            ShouldFailCopy = (_, destination) => destination.EndsWith("orgs.csv", StringComparison.Ordinal)
        };
        PublicationDataset dataset = CreateV2Dataset(guardians: false);
        DateTimeOffset restoreRun = RunUtc.AddHours(-1);
        foreach (string fileName in dataset.CoreFileNames)
        {
            store.AddVersion(
                "output/v2/" + fileName,
                "restore-" + fileName,
                Metadata(restoreRun, "v2", guardians: false),
                restoreRun,
                "old");
        }

        store.FailRestoreNames.Add("output/v2/orgs.csv");

        await Assert.ThrowsAsync<PublicationRollbackException>(() => CreatePublisher(store).PublishAsync(
            dataset,
            "output/v2",
            CancellationToken.None));

        Assert.Equal(dataset.CoreFileNames.Count, store.RestoreAttempts.Count);
    }

    [Fact]
    public async Task VersionLookupFailureIsReportedAsFatalRollbackFailure()
    {
        VersionAwareBlobStore store = new()
        {
            GetVersionsException = new InvalidOperationException("storage unavailable"),
            ShouldFailCopy = (_, destination) => destination.EndsWith("orgs.csv", StringComparison.Ordinal)
        };

        await Assert.ThrowsAsync<PublicationRollbackException>(() => CreatePublisher(store).PublishAsync(
            CreateV2Dataset(false),
            "output/v2",
            CancellationToken.None));
    }

    [Fact]
    public void GuardianEnabledEmptyDatasetsAlwaysContainHeaderOnlyGuardianFiles()
    {
        FileHelper fileHelper = new();

        PublicationDataset v1 = fileHelper.CreateEmptyV1Dataset(includeGuardianSync: true);
        PublicationDataset v2 = fileHelper.CreateEmptyV2Dataset(includeGuardianSync: true);

        Assert.Contains(v1.Files, file => file.Name == "User.csv" && file.Content.ToMemory().Length > 0);
        Assert.Contains(v1.Files, file => file.Name == "Guardianrelationship.csv" && file.Content.ToMemory().Length > 0);
        Assert.Contains(v2.Files, file => file.Name == "relationships.csv" && file.Content.ToMemory().Length > 0);
        Assert.DoesNotContain(fileHelper.CreateEmptyV1Dataset(false).Files, file => file.Name == "User.csv");
        Assert.DoesNotContain(fileHelper.CreateEmptyV2Dataset(false).Files, file => file.Name == "relationships.csv");
    }

    [Fact]
    public void V1DatasetUsesStableAcceptedFileNameCasing()
    {
        PublicationDataset dataset = new FileHelper().CreateEmptyV1Dataset(includeGuardianSync: true);

        Assert.Equal(
            [
                "School.csv",
                "Section.csv",
                "Teacher.csv",
                "Student.csv",
                "TeacherRoster.csv",
                "StudentEnrollment.csv",
                "User.csv",
                "Guardianrelationship.csv"
            ],
            dataset.Files.Select(file => file.Name));
    }

    [Fact]
    public async Task StartupCleanupRecognizesNewAndLegacyOwnedStagingBlobsOnly()
    {
        VersionAwareBlobStore store = new();
        store.AddCurrent(
            $"{OutputPrefix}/.staging/{RunId}/orgs.csv",
            "owned new layout",
            Metadata(RunUtc.AddDays(-1), "v2", false));
        store.AddCurrent(
            $"output/v2/.staging/{RunId}/orgs.csv",
            "owned legacy layout",
            Metadata(RunUtc.AddDays(-1), "v2", false));
        store.AddCurrent(
            "output/.staging/orgs.csv",
            "missing run id",
            Metadata(RunUtc.AddDays(-1), "v2", false));
        store.AddCurrent(
            "output/.staging/0198d4e8fe8c40008000000000000001/orgs.csv",
            "uuid v4",
            Metadata(RunUtc.AddDays(-1), "v2", false));
        store.AddCurrent(
            $"output/.staging/{RunId}/nested/orgs.csv",
            "extra segment",
            Metadata(RunUtc.AddDays(-1), "v2", false));
        store.AddCurrent(
            $"output/.staging/{RunId}/users.csv",
            "missing producer metadata",
            new Dictionary<string, string>());
        store.AddCurrent(
            $"output/.STAGING/{RunId}/classes.csv",
            "wrong staging case",
            Metadata(RunUtc.AddDays(-1), "v2", false));
        store.AddCurrent(
            "archive/.staging/output/v2/orgs.csv",
            "live output folder",
            Metadata(RunUtc.AddDays(-1), "v2", false));
        store.AddCurrent(
            "output/.staging/v2/orgs.csv",
            "live institution",
            Metadata(RunUtc.AddDays(-1), "v2", false));
        store.AddCurrent(
            "output/school/.staging/v2/orgs.csv",
            "live location",
            Metadata(RunUtc.AddDays(-1), "v2", false));
        store.AddCurrent(
            "output/v2/orgs.csv",
            "live",
            Metadata(RunUtc.AddDays(-1), "v2", false));

        await CreatePublisher(store).CleanupStaleStagingAsync(CancellationToken.None);

        Assert.DoesNotContain($"{OutputPrefix}/.staging/{RunId}/orgs.csv", store.Current.Keys);
        Assert.DoesNotContain($"output/v2/.staging/{RunId}/orgs.csv", store.Current.Keys);
        Assert.Contains("output/.staging/orgs.csv", store.Current.Keys);
        Assert.Contains("output/.staging/0198d4e8fe8c40008000000000000001/orgs.csv", store.Current.Keys);
        Assert.Contains($"output/.staging/{RunId}/nested/orgs.csv", store.Current.Keys);
        Assert.Contains($"output/.staging/{RunId}/users.csv", store.Current.Keys);
        Assert.Contains($"output/.STAGING/{RunId}/classes.csv", store.Current.Keys);
        Assert.Contains("archive/.staging/output/v2/orgs.csv", store.Current.Keys);
        Assert.Contains("output/.staging/v2/orgs.csv", store.Current.Keys);
        Assert.Contains("output/school/.staging/v2/orgs.csv", store.Current.Keys);
        Assert.Contains("output/v2/orgs.csv", store.Current.Keys);
    }

    [Fact]
    public async Task StartupCleanupRetriesCompleteCleanupUntilItSucceeds()
    {
        VersionAwareBlobStore store = new()
        {
            StaleCleanupFailuresRemaining = 2
        };
        store.AddCurrent(
            $"{OutputPrefix}/.staging/{RunId}/orgs.csv",
            "owned",
            Metadata(RunUtc.AddDays(-1), "v2", false));

        await CreatePublisher(store).CleanupStaleStagingAsync(CancellationToken.None);

        Assert.Equal(3, store.StaleCleanupAttempts);
        Assert.DoesNotContain($"{OutputPrefix}/.staging/{RunId}/orgs.csv", store.Current.Keys);
    }

    [Fact]
    public async Task ExhaustedStartupCleanupWarnsAndReturnsNormally()
    {
        CollectingLogger logger = new();
        VersionAwareBlobStore store = new()
        {
            StaleCleanupFailuresRemaining = 4
        };

        await new DatasetPublisher(store, logger, OutputPrefix, RunUtc, RunId)
            .CleanupStaleStagingAsync(CancellationToken.None);

        Assert.Equal(4, store.StaleCleanupAttempts);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                entry.Message.Contains("synchronization will continue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DatasetCleanupRetriesEveryKnownBlobAndPreservesPublicationSuccess()
    {
        Dictionary<string, int> attemptsByName = new(StringComparer.Ordinal);
        VersionAwareBlobStore store = new()
        {
            ShouldFailDelete = (_, name) =>
            {
                attemptsByName.TryGetValue(name, out int attempts);
                attemptsByName[name] = ++attempts;
                return name.EndsWith("orgs.csv", StringComparison.Ordinal) && attempts == 1;
            }
        };
        PublicationDataset dataset = CreateV2Dataset(guardians: true);

        DatasetPublicationResult result = await CreatePublisher(store).PublishAsync(
            dataset,
            "output/v2",
            CancellationToken.None);

        Assert.Equal(DatasetPublicationResult.Succeeded, result);
        Assert.All(
            dataset.Files,
            file => Assert.Equal(2, attemptsByName[$"{OutputPrefix}/.staging/{RunId}/{file.Name}"]));
        Assert.DoesNotContain(store.Current.Keys, name => name.Contains("/.staging/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExhaustedDatasetCleanupWarnsWithoutChangingLiveOutputOrSuccess()
    {
        CollectingLogger logger = new();
        VersionAwareBlobStore store = new()
        {
            ShouldFailDelete = (_, name) => name.EndsWith("orgs.csv", StringComparison.Ordinal)
        };
        PublicationDataset dataset = CreateV2Dataset(guardians: true);

        DatasetPublicationResult result = await new DatasetPublisher(store, logger, OutputPrefix, RunUtc, RunId)
            .PublishAsync(dataset, "output/v2", CancellationToken.None);

        Assert.Equal(DatasetPublicationResult.Succeeded, result);
        Assert.Contains("output/v2/orgs.csv", store.Current.Keys);
        Assert.Contains($"{OutputPrefix}/.staging/{RunId}/orgs.csv", store.Current.Keys);
        Assert.Equal(4, store.DeleteAttempts.Count(name => name.EndsWith("orgs.csv", StringComparison.Ordinal)));
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                entry.Message.Contains("published output is unchanged", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CleanupFailureDoesNotReplacePrimaryRollbackFailure()
    {
        CollectingLogger logger = new();
        VersionAwareBlobStore store = new()
        {
            ShouldFailCopy = (_, destination) => destination.EndsWith("orgs.csv", StringComparison.Ordinal),
            ShouldFailDelete = (_, name) => name.Contains("/.staging/", StringComparison.Ordinal)
        };

        PublicationRollbackException exception = await Assert.ThrowsAsync<PublicationRollbackException>(() =>
            new DatasetPublisher(store, logger, OutputPrefix, RunUtc, RunId).PublishAsync(
                CreateV2Dataset(false),
                "output/v2",
                CancellationToken.None));

        Assert.Contains("No complete earlier", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                entry.Message.Contains("staging data", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplicationCancellationDoesNotStartRollback()
    {
        using CancellationTokenSource source = new();
        OperationCanceledException expectedException = new(source.Token);
        VersionAwareBlobStore store = new()
        {
            ShouldFailCopy = (_, _) =>
            {
                source.Cancel();
                throw expectedException;
            }
        };

        OperationCanceledException actualException = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreatePublisher(store).PublishAsync(
            CreateV2Dataset(false),
            "output/v2",
            source.Token));

        Assert.Same(expectedException, actualException);
        Assert.Empty(store.RestoreAttempts);
        Assert.DoesNotContain(
            store.DeleteAttempts,
            name => name.Contains("/.staging/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublicationLogsDoNotContainExceptionMessages()
    {
        CollectingLogger logger = new();
        VersionAwareBlobStore store = new()
        {
            CopyException = new InvalidOperationException("private-value"),
            ShouldFailCopy = (_, _) => true
        };

        await Assert.ThrowsAsync<PublicationRollbackException>(() => new DatasetPublisher(
            store,
            logger,
            OutputPrefix,
            RunUtc,
            RunId).PublishAsync(
                CreateV2Dataset(false),
                "output/v2",
                CancellationToken.None));

        Assert.DoesNotContain(logger.Messages, message => message.Contains("private-value", StringComparison.Ordinal));
    }

    private static DatasetPublisher CreatePublisher(VersionAwareBlobStore store)
    {
        return new DatasetPublisher(store, new CollectingLogger(), OutputPrefix, RunUtc, RunId);
    }

    private static PublicationDataset CreateV2Dataset(bool guardians)
    {
        return new FileHelper().CreateEmptyV2Dataset(guardians);
    }

    private static PublicationDataset CreateCompleteV2Dataset(string scope)
    {
        string[] fileNames = ["orgs.csv", "users.csv", "roles.csv", "classes.csv", "enrollments.csv"];
        return new PublicationDataset(
            "v2",
            guardianEnabled: false,
            fileNames.Select(fileName => new PublicationFile(
                fileName,
                BinaryData.FromString($"{scope}:{fileName}"))).ToArray(),
            fileNames,
            []);
    }

    private static IReadOnlyDictionary<string, string> Metadata(
        DateTimeOffset runUtc,
        string sdsVersion,
        bool guardians)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [DatasetPublisher.ProducerMetadataKey] = DatasetPublisher.ProducerMetadataValue,
            [DatasetPublisher.RunUtcMetadataKey] = runUtc.ToUniversalTime().ToString("O"),
            [DatasetPublisher.SdsVersionMetadataKey] = sdsVersion,
            [DatasetPublisher.GuardiansMetadataKey] = guardians ? "true" : "false"
        };
    }

    private sealed class VersionAwareBlobStore : IBlobPublicationStore
    {
        private readonly List<(BlobRestoreSource Item, BinaryData Content)> _versions = [];
        private long _sequence;

        internal sealed record StoredBlob(
            BinaryData Content,
            IReadOnlyDictionary<string, string> Metadata,
            string VersionId,
            DateTimeOffset LastModified);

        internal Dictionary<string, StoredBlob> Current { get; } = new(StringComparer.Ordinal);

        internal List<string> Operations { get; } = [];

        internal List<(string Name, IReadOnlyDictionary<string, string> Metadata)> Uploads { get; } = [];

        internal List<string> CopyAttempts { get; } = [];

        internal List<string> RestoreAttempts { get; } = [];

        internal List<string> DeleteAttempts { get; } = [];

        internal List<string> RestoredVersionIds { get; } = [];

        internal HashSet<string> FailRestoreNames { get; } = new(StringComparer.Ordinal);

        internal int GetVersionsCalls { get; private set; }

        internal int StaleCleanupAttempts { get; private set; }

        internal int StaleCleanupFailuresRemaining { get; set; }

        internal Func<int, string, bool> ShouldFailCopy { get; init; }

        internal Func<int, string, bool> ShouldFailDelete { get; init; }

        internal Exception CopyException { get; init; } = new InvalidOperationException("copy failed");

        internal Exception GetVersionsException { get; init; }

        public Task EnsureContainerExistsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task UploadAsync(
            string blobName,
            BinaryData content,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("upload:" + blobName);
            Uploads.Add((
                blobName,
                new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase)));
            WriteCurrent(blobName, content, metadata);
            return Task.CompletedTask;
        }

        public Task CopyAsync(
            string sourceBlobName,
            string destinationBlobName,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("copy:" + destinationBlobName);
            CopyAttempts.Add(destinationBlobName);
            if (ShouldFailCopy?.Invoke(CopyAttempts.Count, destinationBlobName) == true)
            {
                throw CopyException;
            }

            WriteCurrent(destinationBlobName, Current[sourceBlobName].Content, metadata);
            return Task.CompletedTask;
        }

        public Task RestoreAsync(
            BlobRestoreSource source,
            string destinationBlobName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreAttempts.Add(destinationBlobName);
            if (FailRestoreNames.Contains(destinationBlobName))
            {
                throw new InvalidOperationException("restore failed");
            }

            if (string.IsNullOrWhiteSpace(source.VersionId))
            {
                if (!string.Equals(source.Name, destinationBlobName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("invalid versionless restore source");
                }

                return Task.CompletedTask;
            }

            BinaryData content = _versions.Single(version =>
                version.Item.Name == source.Name &&
                version.Item.VersionId == source.VersionId).Content;
            WriteCurrent(destinationBlobName, content, source.Metadata);
            RestoredVersionIds.Add(source.VersionId);
            return Task.CompletedTask;
        }

        public Task DeleteIfExistsAsync(string blobName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("delete:" + blobName);
            DeleteAttempts.Add(blobName);
            if (ShouldFailDelete?.Invoke(DeleteAttempts.Count, blobName) == true)
            {
                throw new InvalidOperationException("delete failed");
            }

            if (Current.Remove(blobName, out StoredBlob existing))
            {
                _sequence++;
                _versions.Add((
                    new BlobRestoreSource(
                        blobName,
                        existing.VersionId ?? "version-" + _sequence,
                        existing.LastModified,
                        existing.Metadata),
                    existing.Content));
            }

            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<BlobRestoreSource> GetRestoreSourcesAsync(
            string prefix,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            GetVersionsCalls++;
            if (GetVersionsException is not null)
            {
                throw GetVersionsException;
            }

            foreach ((BlobRestoreSource item, _) in _versions.Where(version =>
                version.Item.Name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }

            foreach ((string name, StoredBlob current) in Current.Where(entry =>
                entry.Key.StartsWith(prefix, StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new BlobRestoreSource(
                    name,
                    current.VersionId,
                    current.LastModified,
                    current.Metadata);
            }

            await Task.CompletedTask;
        }

        public Task DeleteStaleStagingAsync(CancellationToken cancellationToken)
        {
            StaleCleanupAttempts++;
            if (StaleCleanupFailuresRemaining > 0)
            {
                StaleCleanupFailuresRemaining--;
                throw new InvalidOperationException("stale cleanup failed");
            }

            foreach (string name in Current
                .Where(entry => DatasetPublisher.IsOwnedStagingBlob(entry.Key, entry.Value.Metadata))
                .Select(entry => entry.Key)
                .ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                Current.Remove(name);
                Operations.Add("stale-delete:" + name);
            }

            return Task.CompletedTask;
        }

        internal void AddCurrent(
            string name,
            string content,
            IReadOnlyDictionary<string, string> metadata)
        {
            WriteCurrent(name, BinaryData.FromString(content), metadata);
        }

        internal void AddVersion(
            string name,
            string versionId,
            IReadOnlyDictionary<string, string> metadata,
            DateTimeOffset lastModified,
            string content)
        {
            _versions.Add((
                new BlobRestoreSource(name, versionId, lastModified, metadata),
                BinaryData.FromString(content)));
        }

        private void WriteCurrent(
            string name,
            BinaryData content,
            IReadOnlyDictionary<string, string> metadata)
        {
            if (Current.Remove(name, out StoredBlob existing))
            {
                _sequence++;
                _versions.Add((
                    new BlobRestoreSource(
                        name,
                        existing.VersionId ?? "version-" + _sequence,
                        existing.LastModified,
                        existing.Metadata),
                    existing.Content));
            }

            _sequence++;
            Current[name] = new StoredBlob(
                content,
                new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase),
                null,
                RunUtc.AddTicks(_sequence));
        }
    }

    private sealed class CollectingLogger : ILogger<DatasetPublisher>
    {
        internal List<(LogLevel Level, string Message)> Entries { get; } = [];

        internal IEnumerable<string> Messages => Entries.Select(entry => entry.Message);

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NoopScope : IDisposable
        {
            internal static NoopScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
