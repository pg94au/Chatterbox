using AwesomeAssertions;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Chatterbox.Backend.Tests;

public static class ClientWebSocketExtensions
{
    public static async Task SendMessageAsync(this ClientWebSocket clientWebSocket, object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
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

        //received.MessageType.Should().Be(WebSocketMessageType.Text);

        var responseJson = Encoding.UTF8.GetString(receiveBuffer, 0, received.Count);
        var message = JsonSerializer.Deserialize<T>(responseJson);

        return message;
    }
}
