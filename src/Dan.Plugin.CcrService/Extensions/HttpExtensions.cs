using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace Dan.Plugin.CcrService.Extensions;

public static class HttpExtensions
{
    public static readonly HttpRequestOptionsKey<List<HttpStatusCode>> AllowedStatusCodes = new("AllowedStatusCodes");

    /// <summary>
    /// Add allowed error status codes for an individual request
    /// </summary>
    /// <param name="request">The http request message</param>
    /// <param name="codes">The allowed error status codes</param>
    public static void SetAllowedErrorCodes(this HttpRequestMessage request, params HttpStatusCode[] codes)
    {
        request.Options.Set(AllowedStatusCodes, [.. codes]);
    }

    /// <summary>
    /// Get allowed error status codes for an individual request
    /// </summary>
    /// <param name="request">The http request message</param>
    /// <returns>A list of allowed http status codes</returns>
    public static List<HttpStatusCode> GetAllowedErrorCodes(this HttpRequestMessage request)
    {
        return request.Options.TryGetValue(AllowedStatusCodes, out var allowedStatusCodes)
            ? allowedStatusCodes
            : [];
    }
}
