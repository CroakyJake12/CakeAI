using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class CapabilityRegistryTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task Registry_seeds_stable_concrete_first_party_capabilities_without_copying_plugins()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var repository = new CapabilityRepository(database);

        var capabilities = await repository.GetCapabilitiesAsync(CancellationToken.None);

        Assert.Equal(CapabilityRegistryCatalog.BuiltIns.Count, capabilities.Count);
        Assert.Equal(capabilities.Count, capabilities.Select(item => item.Id).Distinct().Count());
        Assert.All(capabilities, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Key));
            Assert.False(string.IsNullOrWhiteSpace(item.ImplementationKey));
            Assert.False(string.IsNullOrWhiteSpace(item.OwnerAppKey));
            Assert.NotEqual(CapabilityPlatform.None, item.Platforms);
        });
    }

    [Fact]
    public async Task First_registry_read_seeds_all_built_ins_with_one_connection()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var factory = new CountingConnectionFactory(database);
        var repository = new CapabilityRepository(factory);

        var first = await repository.GetCapabilitiesAsync(CancellationToken.None);

        Assert.Equal(CapabilityRegistryCatalog.BuiltIns.Count, first.Count);
        Assert.Equal(1, factory.OpenCount);

        var second = await repository.GetCapabilitiesAsync(CancellationToken.None);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(2, factory.OpenCount);
    }

    [Fact]
    public async Task Discovery_filters_by_current_platform_and_keeps_general_first()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var service = new CapabilityRegistryService(new CapabilityRepository(database));

        var android = await service.DiscoverAsync(CapabilityPlatform.Android, CancellationToken.None);

        Assert.DoesNotContain(android, item => item.Key is "powershell" or "run-command" or "run-tests");
        Assert.Contains(android, item => item.Key == "attach-thread");
        Assert.Equal(CapabilityRegistryCatalog.GeneralOwner, android[0].OwnerAppKey);
    }

    [Fact]
    public void Built_in_owners_are_general_or_real_Haven_Apps()
    {
        var appKeys = BuiltInModeSeed.Modes.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(CapabilityRegistryCatalog.BuiltIns, item =>
            Assert.True(item.OwnerAppKey == CapabilityRegistryCatalog.GeneralOwner || appKeys.Contains(item.OwnerAppKey)));
    }

    public void Dispose() => _paths.Dispose();

    private sealed class CountingConnectionFactory(ISqliteConnectionFactory inner) : ISqliteConnectionFactory
    {
        private int _openCount;

        public int OpenCount => Volatile.Read(ref _openCount);

        public async Task<Microsoft.Data.Sqlite.SqliteConnection> OpenAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _openCount);
            return await inner.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-capability-registry-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "legacy.json");
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (IOException) { }
        }
    }
}
