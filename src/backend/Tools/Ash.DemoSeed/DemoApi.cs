using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ash.DemoSeed;

/// <summary>
/// Calls the real API (in-process) as a dev user (the X-Dev-User header, Development only) — the same endpoints, handlers
/// and units of work the UI uses. Any non-success answer stops the seed with the problem text.
/// </summary>
public sealed class DemoApi(Func<HttpClient> createClient)
{
    private readonly Dictionary<string, HttpClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    private HttpClient As(string email)
    {
        if (!_clients.TryGetValue(email, out var client))
        {
            client = createClient();
            client.DefaultRequestHeaders.Add("X-Dev-User", email);
            _clients[email] = client;
        }

        return client;
    }

    public Task<JsonElement> Get(string user, string path) => Send(user, HttpMethod.Get, path, null);

    public Task<JsonElement> Post(string user, string path, object? body = null) => Send(user, HttpMethod.Post, path, body);

    public Task<JsonElement> Put(string user, string path, object body) => Send(user, HttpMethod.Put, path, body);

    public Task<JsonElement> Patch(string user, string path, object body) => Send(user, HttpMethod.Patch, path, body);

    public async Task<JsonElement> Upload(string user, Guid casePublicId, string typeCode, string title, string fileName,
        string contentType, byte[] bytes)
    {
        // the handler reads the fields first, then the file
        using var form = new MultipartFormDataContent
        {
            { new StringContent(typeCode), "documentTypeCode" },
            { new StringContent(title), "title" },
        };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        var response = await As(user).PostAsync($"/api/cases/{casePublicId}/documents", form);
        return await Read(response, user, $"upload {fileName}");
    }

    private async Task<JsonElement> Send(string user, HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await As(user).SendAsync(request);
        return await Read(response, user, $"{method} {path}");
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response, string user, string what)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{what} as {user} → {(int)response.StatusCode}: {text}");
        }

        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
}
