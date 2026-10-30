using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dan.CcrService.Config;

public class Settings
{
    public int DefaultCircuitBreakerOpenCircuitTimeSeconds { get; init; }
    public int DefaultCircuitBreakerFailureBeforeTripping { get; init; }
    public int SafeHttpClientTimeout { get; init; }

    /// <summary>
    /// If the program is running in development environment
    /// </summary>
    public static bool IsDevEnvironment => new[] { "Development", "LocalDevelopment" }.Any(GetSetting("ASPNETCORE_ENVIRONMENT").Contains);

    /// <summary>
    /// If the program is running in staging environment
    /// </summary>
    public static bool IsStagingEnvironment => GetSetting("ASPNETCORE_ENVIRONMENT").Equals("Staging");

    /// <summary>
    /// If the program is running in staging environment
    /// </summary>
    public static bool IsProductionEnvironment => !IsStagingEnvironment && !IsDevEnvironment;

    /// <summary>
    /// Valid subject organizations in development / staging environment (aka test organizations in TT02)
    /// </summary>
    public static List<string> TestEnvironmentValidOrgs => GetSetting("TestEnvironmentValidOrgs").Split(',').ToList();

    /// <summary>
    /// Redis  Connection String
    /// </summary>
    public static string RedisCacheConnectionString => GetSetting("RedisCacheConnectionString");

    private static string GetSetting(string settingKey)
    {
        var value = ConfigurationHelper.ConfigurationRoot[settingKey];
        if (value == null)
        {
            throw new MissingSettingsException($"Missing settings key: {settingKey}");
        }

        return value;
    }

    /// <summary>
    /// Exception thrown if a required setting is missing
    /// </summary>
    public class MissingSettingsException : Exception
    {
        /// <summary>
        /// The constructor
        /// </summary>
        /// <param name="message">The error message</param>
        public MissingSettingsException(string message) : base(message)
        {
        }
    }
}

public static class ConfigurationHelper
{
    public static IConfigurationRoot ConfigurationRoot = null!;
    public static void Initialize(IConfigurationRoot configurationRoot)
    {
        ConfigurationRoot = configurationRoot;
    }
}

