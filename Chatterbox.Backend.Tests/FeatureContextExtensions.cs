using Reqnroll;
using System.Net.WebSockets;

namespace Chatterbox.Backend.Tests;

public static class FeatureContextExtensions
{
    private const string ServiceUrl = "ServiceUrl";
    private const string WebSocketApiId = "WebSocketApiId";
    private const string StageName = "StageName";
    private const string WebSocketConnectionsKey = "WebSocketConnections";

    extension(FeatureContext featureContext)
    {
        public Uri ServiceUrl
        {
            get => featureContext.Get<Uri>(ServiceUrl);
            set => featureContext.Add(ServiceUrl, value);
        }

        public string WebSocketApiId
        {
            get => featureContext.Get<string>(WebSocketApiId);
            set => featureContext.Add(WebSocketApiId, value);
        }

        public string StageName
        {
            get => featureContext.Get<string>(StageName);
            set => featureContext.Add(StageName, value);
        }
    }

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

    public static async Task CleanupWebSocketConnections(this FeatureContext featureContext)
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

        featureContext.Remove(WebSocketConnectionsKey);
    }
}
