namespace Ash.Tests.Integration.Infrastructure;

public static class HttpClientExtensions
{
    /// <summary>Authenticates every request of the client as the seeded user (dev header, Development only).</summary>
    public static HttpClient AsUser(this HttpClient client, string email)
    {
        client.DefaultRequestHeaders.Remove("X-Dev-User");
        client.DefaultRequestHeaders.Add("X-Dev-User", email);
        return client;
    }
}
