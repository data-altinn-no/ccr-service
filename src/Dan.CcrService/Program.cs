using Azure.Core.Serialization;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Dan.CcrService.Config;
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
        services.AddTransient<IEntityRegistryService, EntityRegistryService>();
        services.AddTransient<IEntityRegistryApiClientService, EntityRegistryApiClientService>();
        services.AddTransient<ExceptionDelegatingHandler>();

        services.AddHttpClient("EntityRegistryClient", client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(5);
        })
        .AddHttpMessageHandler<ExceptionDelegatingHandler>();

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
        if (Settings.RedisCacheConnectionString.Contains("password=") ||
            Settings.RedisCacheConnectionString.Contains("127.0.0.1"))
        {
            services.AddStackExchangeRedisCache(option =>
            {
                option.Configuration = Settings.RedisCacheConnectionString;
            });
        }
        else
        {
            services.AddStackExchangeRedisCache(option =>
            {
                option.ConnectionMultiplexerFactory = async () =>
                {
                    var configurationOptions = await ConfigurationOptions
                        .Parse(Settings.RedisCacheConnectionString)
                        .ConfigureForAzureWithTokenCredentialAsync(credentials);

                    var connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions);

                    return connectionMultiplexer;
                };
            });
        }


        var sp = services.BuildServiceProvider();
        var distributedCache = sp.GetRequiredService<IDistributedCache>();
    })
    .Build();

await host.RunAsync();
