using Dan.CcrService.Extensions;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Dan.CcrService.Handlers;
class ExceptionDelegatingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        if (request.Options.TryGetValue(HttpExtensions.AllowedStatusCodes, out var allowedStatusCodes) && allowedStatusCodes.Contains(response.StatusCode))
        {
            return response;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"Server returned error status code: {(int)response.StatusCode}, Body={(string.IsNullOrEmpty(body) ? "<empty>" : body)} ReasonPhrase={(string.IsNullOrEmpty(response.ReasonPhrase) ? "<empty>" : response.ReasonPhrase)}");
    }
}
