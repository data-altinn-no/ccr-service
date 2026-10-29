using Azure.Core.Serialization;
using Azure.Identity;
using Dan.CcrService.Config;
using Dan.CcrService.Handlers;
using Dan.CcrService.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
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
    .ConfigureAppConfiguration((config) =>
    {
        config.AddEnvironmentVariables();
        config.AddJsonFile("host.json", optional: true);
        config.AddJsonFile("worker.json", optional: true);
        ConfigurationHelper.ConfigurationRoot = config.Build();
    })
    .ConfigureServices((context, services) =>
    {
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
