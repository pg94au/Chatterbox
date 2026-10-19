namespace Chatterbox.Backend;

public interface IModelClient
{
    Task<string> GetResponseFromAgentAsync(string request, string requesterName, string? retrievedContext = null);
}
