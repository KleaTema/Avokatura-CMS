using Ash.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;

namespace Ash.AspNetCore.Http;

/// <summary>
/// A download THROUGH THE API (the default, task 032): the endpoint checks access and audits at download time, then
/// returns this result, which streams the stored object — never buffered — with:
/// Content-Type, Content-Length, Content-Disposition: attachment (RFC 5987 file name, Albanian letters survive),
/// X-Content-Type-Options: nosniff and Cache-Control: no-store. The stored object is disposed afterwards.
/// <code>return file is null ? Results.NotFound() : new StoredFileResult(file, document.OriginalFileName);</code>
/// </summary>
public sealed class StoredFileResult(StoredObject file, string fileName) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        await using (file)
        {
            var response = httpContext.Response;
            response.ContentType = file.ContentType;
            response.ContentLength = file.Length;
            response.Headers.ContentDisposition = ContentDisposition.Attachment(fileName);
            response.Headers.XContentTypeOptions = "nosniff";
            response.Headers.CacheControl = "no-store";
            await file.Content.CopyToAsync(response.Body, httpContext.RequestAborted);
        }
    }
}
