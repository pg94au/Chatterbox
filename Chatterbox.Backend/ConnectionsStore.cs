using Amazon.DynamoDBv2.DataModel;

namespace Chatterbox.Backend;

public sealed class ConnectionsStore
{
    private readonly DynamoDBContext _context;
    private readonly string _tableName;

    internal ConnectionsStore(DynamoDBContext context, string tableName)
    {
        _context = context;
        _tableName = tableName;
    }

    internal Task<PresenceRecord?> LoadByDisplayNameAsync(string displayName) =>
        _context.LoadAsync<PresenceRecord>(displayName, CreateTableConfig());

    internal Task SaveAsync(PresenceRecord record) =>
        _context.SaveAsync(record, CreateTableConfig());

    internal Task DeleteByDisplayNameAsync(string displayName) =>
        _context.DeleteAsync<PresenceRecord>(displayName, CreateTableConfig());

    internal async Task<PresenceRecord?> FindByConnectionIdAsync(string connectionId)
    {
        var search = _context.QueryAsync<PresenceRecord>(
            connectionId,
            CreateIndexTableConfig("ConnectionIndex")
        );

        return (await search.GetRemainingAsync()).SingleOrDefault();
    }

    internal async Task<List<PresenceRecord>> GetAllAsync()
    {
        var search = _context.ScanAsync<PresenceRecord>(
            new List<ScanCondition>(),
            CreateTableConfig()
        );

        return await search.GetRemainingAsync();
    }

    private DynamoDBOperationConfig CreateTableConfig() =>
        new()
        {
            OverrideTableName = _tableName
        };

    private DynamoDBOperationConfig CreateIndexTableConfig(string indexName) =>
        new()
        {
            OverrideTableName = _tableName,
            IndexName = indexName
        };
}