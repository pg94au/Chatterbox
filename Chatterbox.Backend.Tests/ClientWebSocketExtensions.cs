using AwesomeAssertions;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Chatterbox.Backend.Tests;

public static class ClientWebSocketExtensions
{
    private static readonly IReadOnlyDictionary<string, Type> MessageTypes = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["registered"] = typeof(RegisteredEvent),
        ["userJoined"] = typeof(UserJoinedEvent),
        ["userLeft"] = typeof(UserLeftEvent),
        ["kicked"] = typeof(KickedEvent),
        ["error"] = typeof(ErrorEvent),
        ["message"] = typeof(MessageEvent),
        ["users"] = typeof(UsersEvent)
    };

    public static async Task SendMessageAsync(this ClientWebSocket clientWebSocket, object message, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        await clientWebSocket.SendAsync(
            new ArraySegment<byte>(payload),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    public static async Task<T?> ReceiveMessage<T>(this ClientWebSocket clientWebSocket, CancellationToken cancellationToken)
    {
        var receiveBuffer = new byte[4096];
        var received = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cancellationToken);

        received.MessageType.Should().Be(WebSocketMessageType.Text);

        var responseJson = Encoding.UTF8.GetString(receiveBuffer, 0, received.Count);
        EnsureExpectedEventType<T>(responseJson);

        return JsonSerializer.Deserialize<T>(responseJson);
    }

    public static async Task NothingReceived(this ClientWebSocket clientWebSocket, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var tooLongCts = new CancellationTokenSource(timeout.Add(TimeSpan.FromMilliseconds(100)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, tooLongCts.Token);

        var receiveBuffer = new byte[4096];
        try
        {
            var received = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), linkedCts.Token);

            received.Should().NotBeNull("did not expect to receive message before timeout specified");
        }
        catch (TaskCanceledException e) when (timeoutCts.IsCancellationRequested)
        {
            // Expected timeout
        }
    }

    private static void EnsureExpectedEventType<T>(string responseJson)
    {
        var expectedClrType = typeof(T);
        if (!MessageTypes.Values.Contains(expectedClrType))
        {
            return;
        }

        var receivedType = GetReceivedEventType(responseJson)
            ?? throw new InvalidOperationException($"Expected '{expectedClrType.Name}', but received payload had no 'type' property.");

        if (!MessageTypes.TryGetValue(receivedType, out var receivedClrType))
        {
            throw new InvalidOperationException($"Expected '{expectedClrType.Name}', but received unknown message type '{receivedType}'.");
        }

        if (receivedClrType != expectedClrType)
        {
            throw new InvalidOperationException($"Expected '{expectedClrType.Name}', but received '{receivedClrType.Name}'.");
        }
    }

    private static string? GetReceivedEventType(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);

        if (!document.RootElement.TryGetProperty("type", out var receivedTypeElement))
        {
            return null;
        }

        return receivedTypeElement.GetString();
    }
}
