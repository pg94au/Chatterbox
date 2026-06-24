using Amazon.DynamoDBv2.DataModel;

namespace Chatterbox.Backend;

[DynamoDBTable("ignored-by-operation-config")]
public class PresenceRecord
{
    [DynamoDBHashKey("displayName")]
    public string DisplayName { get; set; } = "";

    [DynamoDBProperty("connectionId")]
    [DynamoDBGlobalSecondaryIndexHashKey("ConnectionIndex")]
    public string ConnectionId { get; set; } = "";

    [DynamoDBProperty("connectedAt")]
    public long ConnectedAt { get; set; }
}