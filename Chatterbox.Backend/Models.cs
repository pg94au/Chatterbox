using System.Text.Json.Serialization;

namespace Chatterbox.Backend;

public record RegisterRequest(string DisplayName)
{
    [JsonPropertyName("action")]
    public string Action => "register";
};

public record ListUsersRequest()
{
    [JsonPropertyName("action")]
    public string Action => "listUsers";
}

public record MessageRequest(
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("text")] string Text
)
{
    [JsonPropertyName("action")] public string Action => "message";
}

public record RegisteredEvent([property: JsonPropertyName("displayName")] string DisplayName)
{
    [JsonPropertyName("type")]
    public string Type => "registered";
}

public record UserJoinedEvent([property: JsonPropertyName("displayName")] string DisplayName)
{
    [JsonPropertyName("type")]
    public string Type => "userJoined";
}

public record UserLeftEvent([property: JsonPropertyName("displayName")] string DisplayName)
{
    [JsonPropertyName("type")]
    public string Type => "userLeft";
}

public record KickedEvent([property: JsonPropertyName("reason")] string Reason)
{
    [JsonPropertyName("type")]
    public string Type => "kicked";
}

public record ErrorEvent([property: JsonPropertyName("error")] string Error)
{
    [JsonPropertyName("type")]
    public string Type => "error";
}

public record MessageEvent(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("text")] string Text
    )
{
    [JsonPropertyName("type")]
    public string Type => "message";
}

public record UsersEvent([property: JsonPropertyName("users")] IEnumerable<UserPresence> Users)
{
    [JsonPropertyName("type")]
    public string Type => "users";
}

public record UserPresence(
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("connectedAt")] long ConnectedAt
);
