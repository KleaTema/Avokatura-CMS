using Ash.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.Net.Http.Headers;

namespace Ash.AspNetCore.Http;

/// <summary>
/// A download THROUGH THE API (the default, task 032): the endpoint checks access and audits at download time, then
/// returns this result, which streams the stored object — never buffered — with:
/// Content-Type, Content-Length, Content-Disposition (RFC 5987 file name, Albanian letters survive),
/// X-Content-Type-Options: nosniff and Cache-Control: no-store. The stored object is disposed afterwards.
/// <code>return file is null ? Results.NotFound() : new StoredFileResult(file, document.OriginalFileName);</code>
/// <see cref="FromStorage"/> (task 034) opens the object itself and answers a single "Range: bytes=…" request with
/// 206 Partial Content (a PDF viewer reads a large file in parts), 416 when the range is outside the file.
/// </summary>
public sealed class StoredFileResult : IResult
{
    private readonly StoredObject? _file;
    private readonly Source? _source;
    private readonly string _fileName;

    public StoredFileResult(StoredObject file, string fileName)
    {
        _file = file;
        _fileName = fileName;
    }

    private StoredFileResult(Source source, string fileName)
    {
        _source = source;
        _fileName = fileName;
    }

    private sealed record Source(
        IFileStorage Storage, string Bucket, string Key, long Length, string ContentType, bool Inline,
        IReadOnlyDictionary<string, string> Headers);

    /// <param name="length">The object's length as recorded (documents.size_bytes): ranges are checked against it.</param>
    /// <param name="inline">Content-Disposition inline (preview) instead of attachment (download).</param>
    /// <param name="headers">Extra response headers, e.g. a Content-Security-Policy for a preview.</param>
    public static StoredFileResult FromStorage(
        IFileStorage storage, string bucket, string key, long length, string contentType, string fileName,
        bool inline = false, IReadOnlyDictionary<string, string>? headers = null) =>
        new(new Source(storage, bucket, key, length, contentType, inline, headers ?? new Dictionary<string, string>()), fileName);

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        if (_file is not null)
        {
            await using (_file)
            {
                SafeHeaders(httpContext.Response, ContentDisposition.Attachment(_fileName));
                httpContext.Response.ContentType = _file.ContentType;
                httpContext.Response.ContentLength = _file.Length;
                await _file.Content.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
            }

            return;
        }

        await ExecuteFromStorageAsync(httpContext, _source!);
    }

    private async Task ExecuteFromStorageAsync(HttpContext httpContext, Source source)
    {
        var response = httpContext.Response;
        var range = RequestedRange(httpContext.Request, source.Length);
        if (range is { Satisfiable: false })
        {
            response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            response.Headers.ContentRange = $"bytes */{source.Length}";
            response.Headers.AcceptRanges = "bytes";
            return;
        }

        var ct = httpContext.RequestAborted;
        var file = range is { } r
            ? await source.Storage.OpenReadAsync(source.Bucket, source.Key, r.From, r.To, ct)
            : await source.Storage.OpenReadAsync(source.Bucket, source.Key, ct);
        if (file is null)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await using (file)
        {
            SafeHeaders(response, source.Inline ? ContentDisposition.Inline(_fileName) : ContentDisposition.Attachment(_fileName));
            foreach (var (name, value) in source.Headers)
            {
                response.Headers[name] = value;
            }

            response.Headers.AcceptRanges = "bytes";
            response.ContentType = source.ContentType;
            response.ContentLength = file.Length;
            if (range is { } partial)
            {
                response.StatusCode = StatusCodes.Status206PartialContent;
                response.Headers.ContentRange = $"bytes {partial.From}-{partial.To}/{source.Length}";
            }

            await file.Content.CopyToAsync(response.Body, ct);
        }
    }

    private static void SafeHeaders(HttpResponse response, string disposition)
    {
        response.Headers.ContentDisposition = disposition;
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = "no-store";
    }

    private readonly record struct ByteRange(long From, long To, bool Satisfiable);

    /// <summary>
    /// One "bytes" range resolved against the length (inclusive bounds); null = send the whole file: no Range header,
    /// several ranges, another unit, or an If-Range (there is no validator to compare it with, RFC 9110 says send all).
    /// </summary>
    private static ByteRange? RequestedRange(HttpRequest request, long length)
    {
        RequestHeaders headers = request.GetTypedHeaders();
        if (headers.Range is not { } header || request.Headers.ContainsKey(HeaderNames.IfRange)
            || !header.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase) || header.Ranges.Count != 1)
        {
            return null;
        }

        var item = header.Ranges.Single();
        if (item.From is { } from)
        {
            return from >= length
                ? new ByteRange(0, 0, false)
                : new ByteRange(from, Math.Min(item.To ?? length - 1, length - 1), true);
        }

        // suffix: the last N bytes
        return item.To is { } last and > 0 && length > 0
            ? new ByteRange(Math.Max(0, length - last), length - 1, true)
            : new ByteRange(0, 0, false);
    }
}
