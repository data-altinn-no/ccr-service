using Azure.Core;
using Azure.Core.Serialization;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Dan.CcrService.Config;
using Dan.CcrService.Extensions;
using Dan.CcrService.Handlers;
using Dan.CcrService.Services;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(_ => { }, options =>
    {
        options.Serializer = new NewtonsoftJsonObjectSerializer(
            new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore
            });
    })
    .ConfigureLogging(logging =>
    {
        logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
        });
    })
    .ConfigureAppConfiguration((config) =>
    {
        config.AddEnvironmentVariables();
        config.AddJsonFile("host.json", optional: true);
        config.AddJsonFile("worker.json", optional: true);
        ConfigurationHelper.ConfigurationRoot = config.Build();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddLogging();
        // UseAzureMonitorExporter() throws at startup if no connection string is configured,
        // so it's gated here to keep plugins working locally without one.
        var appInsightsConnectionString = context.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        var instrumentationKey = context.Configuration["APPINSIGHTS_INSTRUMENTATIONKEY"];

        if (string.IsNullOrWhiteSpace(appInsightsConnectionString) && !string.IsNullOrWhiteSpace(instrumentationKey))
        {
            appInsightsConnectionString = $"InstrumentationKey={instrumentationKey}";
        }

        if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
        {
            services.AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddHttpClientInstrumentation())
                .UseAzureMonitorExporter(options => options.ConnectionString = appInsightsConnectionString)
                .UseFunctionsWorkerDefaults();
        }

        services.Configure<Settings>(context.Configuration);
        var settings = context.Configuration.Get<Settings>();
        services.AddTransient<IEntityRegistryService, EntityRegistryService>();
        services.AddTransient<IEntityRegistryApiClientService, EntityRegistryApiClientService>();
        services.AddTransient<ExceptionDelegatingHandler>();

        services.AddHttpClient("EntityRegistryClient", client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(5);
        })
        .AddHttpMessageHandler<ExceptionDelegatingHandler>()
        .AddPerHostResilience(TimeSpan.FromSeconds(5));

        DefaultAzureCredentialOptions options = new()
        {
            Diagnostics =
            {
                LoggedHeaderNames = { "x-ms-request-id" },
                LoggedQueryParameters = { "api-version" },
                IsAccountIdentifierLoggingEnabled = true
            }
        };
        DefaultAzureCredential credentials = new(options);
        services.AddSingleton(credentials);
        // In case of still using access key (or local redis),
        TokenCredential credential = new DefaultAzureCredential();
        if (Settings.RedisCacheConnectionString.Contains("password=") ||
            Settings.RedisCacheConnectionString.Contains("127.0.0.1"))
        {
            services.AddStackExchangeRedisCache(option =>
            {
                option.Configuration = Settings.RedisCacheConnectionString;
            });
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(Settings.RedisCacheConnectionString));
        }
        else
        {
            var configurationOptions = ConfigurationOptions
                .Parse(Settings.RedisCacheConnectionString)
                .ConfigureForAzureWithTokenCredentialAsync(credential)
                .GetAwaiter().GetResult();

            IConnectionMultiplexer connectionMultiplexer = ConnectionMultiplexer.Connect(configurationOptions);
            services.AddSingleton(connectionMultiplexer);
            services.AddStackExchangeRedisCache(option =>
            {
                option.ConnectionMultiplexerFactory = () => Task.FromResult(connectionMultiplexer);
            });
        }

        services.AddMemoryCache();


        var sp = services.BuildServiceProvider();
        var distributedCache = sp.GetRequiredService<IDistributedCache>();
    })
    .Build();

await host.RunAsync();
