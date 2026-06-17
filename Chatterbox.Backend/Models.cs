namespace Chatterbox.Backend;

public record RegisterRequest(string DisplayName);

public record MessageRequest(string To, string Text);

public record RegisteredEvent(string DisplayName)
{
    public string Type => "registered";
}

public record UserJoinedEvent(string DisplayName)
{
    public string Type => "userJoined";
}

public record UserLeftEvent(string DisplayName)
{
    public string Type => "userLeft";
}

public record KickedEvent(string Reason)
{
    public string Type => "kicked";
}

public record ErrorEvent(string Error)
{
    public string Type => "error";
}

public record MessageEvent(string From, string Text)
{
    public string Type => "message";
}

public record UsersEvent(IEnumerable<object> Users)
{
    public string Type => "users";
}