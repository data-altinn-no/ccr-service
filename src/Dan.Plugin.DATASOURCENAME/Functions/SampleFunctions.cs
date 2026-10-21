using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Dan.Common;
using Dan.Common.Exceptions;
using Dan.Common.Interfaces;
using Dan.Common.Models;
using Dan.Common.Util;
using Dan.Plugin.DATASOURCENAME.Config;
using Dan.Plugin.DATASOURCENAME.Models.Dtos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Dan.Plugin.DATASOURCENAME.Functions;

public class SampleFunctions(
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    IOptions<Settings> settings,
    IEvidenceSourceMetadata evidenceSourceMetadata)
{
    private readonly ILogger logger = loggerFactory.CreateLogger<SampleFunctions>();
    private readonly HttpClient client = httpClientFactory.CreateClient(Constants.SafeHttpClient);
    private readonly Settings settings = settings.Value;

    [Function(PluginConstants.SimpleDatasetName)]
    public async Task<HttpResponseData> GetSimpleDatasetAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestData req,
        FunctionContext context)
    {

        logger.LogDebug("debug HERE");
        logger.LogWarning("warning HERE");
        logger.LogError("error HERE");

        var evidenceHarvesterRequest = await req.ReadFromJsonAsync<EvidenceHarvesterRequest>();

        return await EvidenceSourceResponse.CreateResponse(req,
            () => GetEvidenceValuesSimpledataset(evidenceHarvesterRequest));
    }

    [Function(PluginConstants.RichDatasetName)]
    public async Task<HttpResponseData> GetRichDatasetAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestData req,
        FunctionContext context)
    {
        var evidenceHarvesterRequest = await req.ReadFromJsonAsync<EvidenceHarvesterRequest>();

        return await EvidenceSourceResponse.CreateResponse(req,
            () => GetEvidenceValuesRichDataset(evidenceHarvesterRequest));
    }

    private async Task<List<EvidenceValue>> GetEvidenceValuesSimpledataset(EvidenceHarvesterRequest evidenceHarvesterRequest)
    {
        var url = settings.EndpointUrl + "?someparameter=" + evidenceHarvesterRequest.OrganizationNumber;
        var exampleModel = await MakeRequest<ExampleModel>(url);

        var ecb = new EvidenceBuilder(evidenceSourceMetadata, PluginConstants.SimpleDatasetName);
        ecb.AddEvidenceValue("field1", exampleModel.ResponseField1, PluginConstants.SourceName);
        ecb.AddEvidenceValue("field2", exampleModel.ResponseField2, PluginConstants.SourceName);

        return ecb.GetEvidenceValues();
    }

    private async Task<List<EvidenceValue>> GetEvidenceValuesRichDataset(EvidenceHarvesterRequest evidenceHarvesterRequest)
    {

        var url = settings.EndpointUrl + "?someparameter=" + evidenceHarvesterRequest.OrganizationNumber;
        var exampleModel = await MakeRequest<ExampleModel>(url);

        var ecb = new EvidenceBuilder(evidenceSourceMetadata, PluginConstants.RichDatasetName);

        ecb.AddEvidenceValue("default", exampleModel, PluginConstants.SourceName);

        return ecb.GetEvidenceValues();
    }

    private async Task<T> MakeRequest<T>(string target)
    {
        HttpResponseMessage result;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, target);
            result = await client.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            throw new EvidenceSourceTransientException(PluginConstants.ErrorUpstreamUnavailble, "Error communicating with upstream source", ex);
        }

        if (!result.IsSuccessStatusCode)
        {
            throw result.StatusCode switch
            {
                HttpStatusCode.NotFound => new EvidenceSourcePermanentClientException(PluginConstants.ErrorNotFound, "Upstream source could not find the requested entity (404)"),
                HttpStatusCode.BadRequest => new EvidenceSourcePermanentClientException(PluginConstants.ErrorInvalidInput,  "Upstream source indicated an invalid request (400)"),
                _ => new EvidenceSourceTransientException(PluginConstants.ErrorUpstreamUnavailble, $"Upstream source retuned an HTTP error code ({(int)result.StatusCode})")
            };
        }

        try
        {
            return JsonConvert.DeserializeObject<T>(await result.Content.ReadAsStringAsync());
        }
        catch (Exception ex)
        {
            logger.LogError("Unable to parse data returned from upstream source: {exceptionType}: {exceptionMessage}", ex.GetType().Name, ex.Message);
            throw new EvidenceSourcePermanentServerException(PluginConstants.ErrorUnableToParseResponse, "Could not parse the data model returned from upstream source", ex);
        }
    }
}
