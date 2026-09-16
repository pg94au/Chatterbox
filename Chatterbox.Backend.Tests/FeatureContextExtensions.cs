using Reqnroll;
using System.Net.WebSockets;

namespace Chatterbox.Backend.Tests;

public static class FeatureContextExtensions
{
    private const string WebSocketConnectionsKey = "WebSocketConnections";

    public static ClientWebSocket GetWebSocketConnection(this FeatureContext featureContext, string websocketName)
    {
        var webSockets = featureContext.ContainsKey(WebSocketConnectionsKey)
            ? featureContext.Get<Dictionary<string, ClientWebSocket>>(WebSocketConnectionsKey)
            : new Dictionary<string, ClientWebSocket>();

        if (webSockets.TryGetValue(websocketName, out var existingWebSocket))
        {
            return existingWebSocket;
        }

        var newWebSocket = new ClientWebSocket();
        webSockets[websocketName] = newWebSocket;
        featureContext.Set(webSockets, WebSocketConnectionsKey);

        return newWebSocket;
    }

    public static async Task DisposeWebSocketConnections(this FeatureContext featureContext)
    {
        if (featureContext.ContainsKey(WebSocketConnectionsKey))
        {
            var webSockets = featureContext.Get<Dictionary<string, ClientWebSocket>>(WebSocketConnectionsKey);
            foreach (var clientWebSocket in webSockets.Values)
            {
                try
                {
                    if (clientWebSocket.State == WebSocketState.Open)
                    {
                        await clientWebSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Scenario complete",
                            CancellationToken.None
                        );
                    }

                    clientWebSocket.Dispose();
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }
    }
}
