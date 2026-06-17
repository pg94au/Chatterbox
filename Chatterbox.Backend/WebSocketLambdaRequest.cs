namespace Chatterbox.Backend;

public sealed class WebSocketLambdaRequest
{
    public WebSocketRequestContext? RequestContext { get; init; }

    public string? Body { get; init; }
}