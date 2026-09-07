using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class CapabilityRepository(ISqliteConnectionFactory factory) : ICapabilityRepository
{
    private readonly SemaphoreSlim _seedGate = new(1, 1);
    private bool _seeded;

    public async Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SeedAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM capabilities WHERE is_enabled=1 ORDER BY owner_app_key,name;";
        var result = new List<CapabilityDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(Read(reader));
        return result;
    }

    public async Task UpsertCapabilityAsync(CapabilityDefinition capability, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = UpsertSql;
        Bind(command, capability);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetCapabilityEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE capabilities SET is_enabled=$enabled,updated_at=$updatedAt WHERE id=$id;";
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteCustomCapabilityAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM capabilities WHERE id=$id AND is_built_in=0;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedAsync(Microsoft.Data.Sqlite.SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _seeded)) return;

        await _seedGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _seeded)) return;

            await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var capability in CapabilityRegistryCatalog.BuiltIns)
                await UpsertBuiltInAsync(connection, transaction, capability, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _seeded, true);
        }
        finally
        {
            _seedGate.Release();
        }
    }

    private static async Task UpsertBuiltInAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        CapabilityDefinition capability,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = UpsertSql.Replace(
            "is_built_in=excluded.is_built_in,is_enabled=excluded.is_enabled",
            "is_built_in=1,is_enabled=1",
            StringComparison.Ordinal);
        Bind(command, capability with { IsBuiltIn = true, IsEnabled = true });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string UpsertSql = """
        INSERT INTO capabilities(
          id,key,name,description,owner_app_key,icon_key,instructions,implementation_key,
          semantic_actions_json,platforms,risk_class,availability,dependencies_json,provider_id,
          is_attachable,is_agent_usable,is_built_in,is_enabled,updated_at)
        VALUES(
          $id,$key,$name,$description,$ownerAppKey,$iconKey,$instructions,$implementationKey,
          $semanticActionsJson,$platforms,$riskClass,$availability,$dependenciesJson,$providerId,
          $isAttachable,$isAgentUsable,$isBuiltIn,$isEnabled,$updatedAt)
        ON CONFLICT(id) DO UPDATE SET
          key=excluded.key,name=excluded.name,description=excluded.description,owner_app_key=excluded.owner_app_key,
          icon_key=excluded.icon_key,instructions=excluded.instructions,implementation_key=excluded.implementation_key,
          semantic_actions_json=excluded.semantic_actions_json,platforms=excluded.platforms,risk_class=excluded.risk_class,
          availability=excluded.availability,dependencies_json=excluded.dependencies_json,provider_id=excluded.provider_id,
          is_attachable=excluded.is_attachable,is_agent_usable=excluded.is_agent_usable,
          is_built_in=excluded.is_built_in,is_enabled=excluded.is_enabled,updated_at=excluded.updated_at;
        """;

    private static CapabilityDefinition Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        reader.Guid("id"), reader.String("key"), reader.String("name"), reader.String("description"),
        reader.String("owner_app_key"), reader.String("icon_key"), reader.String("instructions"),
        reader.String("implementation_key"), reader.String("semantic_actions_json"),
        (CapabilityPlatform)reader.Int32("platforms"), (CapabilityRiskClass)reader.Int32("risk_class"),
        (CapabilityAvailability)reader.Int32("availability"), reader.String("dependencies_json"),
        reader.String("provider_id"), reader.Boolean("is_attachable"), reader.Boolean("is_agent_usable"),
        reader.Boolean("is_built_in"), reader.Boolean("is_enabled"), reader.DateTimeOffset("updated_at"));

    private static void Bind(Microsoft.Data.Sqlite.SqliteCommand command, CapabilityDefinition item)
    {
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$key", item.Key);
        command.Parameters.AddWithValue("$name", item.Name);
        command.Parameters.AddWithValue("$description", item.Description);
        command.Parameters.AddWithValue("$ownerAppKey", item.OwnerAppKey);
        command.Parameters.AddWithValue("$iconKey", item.IconKey);
        command.Parameters.AddWithValue("$instructions", item.Instructions);
        command.Parameters.AddWithValue("$implementationKey", item.ImplementationKey);
        command.Parameters.AddWithValue("$semanticActionsJson", item.SemanticActionsJson);
        command.Parameters.AddWithValue("$platforms", (int)item.Platforms);
        command.Parameters.AddWithValue("$riskClass", (int)item.RiskClass);
        command.Parameters.AddWithValue("$availability", (int)item.Availability);
        command.Parameters.AddWithValue("$dependenciesJson", item.DependenciesJson);
        command.Parameters.AddWithValue("$providerId", item.ProviderId);
        command.Parameters.AddWithValue("$isAttachable", item.IsAttachable ? 1 : 0);
        command.Parameters.AddWithValue("$isAgentUsable", item.IsAgentUsable ? 1 : 0);
        command.Parameters.AddWithValue("$isBuiltIn", item.IsBuiltIn ? 1 : 0);
        command.Parameters.AddWithValue("$isEnabled", item.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", item.UpdatedAt.ToString("O"));
    }
}
