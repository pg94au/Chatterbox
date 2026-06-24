using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;

namespace Chatterbox.Backend;

public sealed class ConnectionsStore
{
    private readonly IAmazonDynamoDB _client;
    private readonly DynamoDBContext _context;
    private readonly string _tableName;

    internal ConnectionsStore(IAmazonDynamoDB client, DynamoDBContext context, string tableName)
    {
        _client = client;
        _context = context;
        _tableName = tableName;
    }

    internal Task<PresenceRecord?> LoadByDisplayNameAsync(string displayName) =>
        _context.LoadAsync<PresenceRecord?>(displayName, CreateTableConfig());

    internal Task SaveAsync(PresenceRecord record) =>
        _context.SaveAsync(record, CreateTableConfig());

    internal Task DeleteByDisplayNameAsync(string displayName) =>
        _context.DeleteAsync<PresenceRecord>(displayName, CreateTableConfig());

    internal async Task<PresenceRecord?> FindByConnectionIdAsync(string connectionId)
    {
        var response = await _client.QueryAsync(new QueryRequest
        {
            TableName = _tableName,
            IndexName = "ConnectionIndex",
            KeyConditionExpression = "#connectionId = :connectionId",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#connectionId"] = "connectionId"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":connectionId"] = new AttributeValue { S = connectionId }
            },
            Limit = 1
        }
        );

        return response.Items.Count == 0 ? null : ToPresenceRecord(response.Items[0]);
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

    private static PresenceRecord ToPresenceRecord(Dictionary<string, AttributeValue> item) =>
        new()
        {
            DisplayName = item.TryGetValue("displayName", out var displayName) ? displayName.S : "",
            ConnectionId = item.TryGetValue("connectionId", out var connectionId) ? connectionId.S : "",
            ConnectedAt = item.TryGetValue("connectedAt", out var connectedAt) && long.TryParse(connectedAt.N, out var value)
                ? value
                : 0
        };
}