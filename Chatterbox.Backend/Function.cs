using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Amazon.Lambda.Annotations;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Microsoft.Extensions.Logging;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Chatterbox.Backend;

public class Functions
{
    private readonly ConnectionsStore _connectionsStore;
    private readonly ILogger<Functions> _logger;

    public Functions(ConnectionsStore connectionsStore, ILogger<Functions> logger)
    {
        _connectionsStore = connectionsStore;
        _logger = logger;

        _logger.LogInformation("Functions class initialized.");
    }

    [LambdaFunction]
    public async Task<APIGatewayProxyResponse> Handler(APIGatewayProxyRequest request)
    {
        _logger.LogInformation("Lambda handler invoked for route {RouteKey} and needs to process the request.", request.RequestContext.RouteKey);

        var routeKey = request.RequestContext.RouteKey;

        switch (routeKey)
        {
            case "$connect":
                await HandleConnect(request);
                break;

            case "$disconnect":
                await HandleDisconnect(request);
                break;

            case "register":
                await HandleRegister(request);
                break;

            case "listUsers":
                await HandleListUsers(request);
                break;

            case "message":
                await HandleMessage(request);
                break;

            default:
                break;
        }

        return new APIGatewayProxyResponse { StatusCode = 200 };
    }

    private async Task HandleConnect(APIGatewayProxyRequest request)
    {
        await Task.CompletedTask;
    }

    private async Task HandleRegister(APIGatewayProxyRequest request)
    {
        var body =
            JsonSerializer.Deserialize<RegisterRequest>(
                request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            )
            ?? throw new InvalidOperationException();

        var connectionId = request.RequestContext.ConnectionId;

        var apiClient = CreateManagementClient(request);

        if (string.IsNullOrWhiteSpace(body.DisplayName))
        {
            await SendToConnection(
                apiClient,
                connectionId,
                new ErrorEvent("displayName_required")
            );

            return;
        }

        // Is this connection already registered?
        var existingConnection = await _connectionsStore.FindByConnectionIdAsync(connectionId);
        if (existingConnection is not null)
        {
            await SendToConnection(
                apiClient,
                connectionId,
                new ErrorEvent("already_registered")
            );

            return;
        }

        // Does this display name already exist?
        var existingName = await _connectionsStore.LoadByDisplayNameAsync(body.DisplayName);
        if (existingName is not null)
        {
            try
            {
                await SendToConnection(
                    apiClient,
                    existingName.ConnectionId,
                    new KickedEvent("Another session registered using this name.")
                );

                await apiClient.DeleteConnectionAsync(
                    new DeleteConnectionRequest
                    {
                        ConnectionId = existingName.ConnectionId
                    }
                );
            }
            catch
            {
                // Ignore stale connections.
            }
        }

        await _connectionsStore.SaveAsync(
            new PresenceRecord
            {
                DisplayName = body.DisplayName,
                ConnectionId = connectionId,
                ConnectedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

        var isNewUser = existingName is null;
        if (isNewUser)
        {
            await Broadcast(apiClient, new UserJoinedEvent(body.DisplayName));
        }

        await SendToConnection(apiClient, connectionId, new RegisteredEvent(body.DisplayName));
    }

    private async Task HandleDisconnect(APIGatewayProxyRequest request)
    {
        var connectionId = request.RequestContext.ConnectionId;

        var record = await _connectionsStore.FindByConnectionIdAsync(connectionId);
        if (record is null)
        {
            return;
        }

        await _connectionsStore.DeleteByDisplayNameAsync(record.DisplayName);

        await Broadcast(CreateManagementClient(request), new UserLeftEvent(record.DisplayName));
    }

    private async Task HandleListUsers(APIGatewayProxyRequest request)
    {
        var apiClient = CreateManagementClient(request);

        var users = await _connectionsStore.GetAllAsync();

        await SendToConnection(
            apiClient,
            request.RequestContext.ConnectionId,
            new UsersEvent(users.Select(item => new
            {
                DisplayName = item.DisplayName,
                ConnectedAt = item.ConnectedAt
            }))
        );
    }

    private async Task HandleMessage(APIGatewayProxyRequest request)
    {
        var body =
            JsonSerializer.Deserialize<MessageRequest>(request.Body)
            ?? throw new InvalidOperationException();

        var senderConnectionId = request.RequestContext.ConnectionId;

        var apiClient = CreateManagementClient(request);

        var sender = await _connectionsStore.FindByConnectionIdAsync(senderConnectionId);
        if (sender is null)
        {
            await SendToConnection(
                apiClient,
                senderConnectionId,
                new ErrorEvent("sender_not_registered")
            );

            return;
        }

        var recipient = await _connectionsStore.LoadByDisplayNameAsync(body.To);

        if (recipient is null)
        {
            await SendToConnection(apiClient, senderConnectionId, new ErrorEvent("user_not_online"));

            return;
        }

        await SendToConnection(
            apiClient,
            recipient.ConnectionId,
            new MessageEvent(
                sender.DisplayName,
                body.Text
            )
        );
    }

    private IAmazonApiGatewayManagementApi CreateManagementClient(APIGatewayProxyRequest request)
    {
        var endpoint = $"https://{request.RequestContext.DomainName}/{request.RequestContext.Stage}";

        return new AmazonApiGatewayManagementApiClient(
            new AmazonApiGatewayManagementApiConfig
            {
                ServiceURL = endpoint
            }
        );
    }

    private async Task Broadcast(IAmazonApiGatewayManagementApi apiClient, object payload)
    {
        var connections = await _connectionsStore.GetAllAsync();

        foreach (var item in connections)
        {
            try
            {
                await SendToConnection(apiClient, item.ConnectionId, payload);
            }
            catch
            {
            }
        }
    }

    private static async Task SendToConnection(IAmazonApiGatewayManagementApi apiClient, string connectionId, object payload)
    {
        await apiClient.PostToConnectionAsync(
            new PostToConnectionRequest
            {
                ConnectionId = connectionId,
                Data = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(payload))
            }
        );
    }
}