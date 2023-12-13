// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Azure.AI.OpenAI;
using Aspire.Azure.Common;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Core.Extensions;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Provides extension methods for registering <see cref="OpenAIClient"/> as a singleton in the services provided by the <see cref="IHostApplicationBuilder"/>.
/// </summary>
public static class AspireAzureAIOpenAIExtensions
{
    private const string DefaultConfigSectionName = "Aspire:Azure:AI:OpenAI";
    /// <summary>
    /// Registers <see cref="OpenAIClient"/> as a singleton in the services provided by the <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="connectionName">A name used to retrieve the connection string from the ConnectionStrings configuration section.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="AzureOpenAISettings"/>. It's invoked after the settings are read from the configuration.</param>
    /// <param name="configureClientBuilder">An optional method that can be used for customizing the <see cref="IAzureClientBuilder{OpenAIClient, OpenAIClientOptions}"/>.</param>
    /// <remarks>Reads the configuration from "Aspire.Azure.AI.OpenAI" section.</remarks>
    public static void AddAzureAIOpenAI(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<AzureOpenAISettings>? configureSettings = null,
        Action<IAzureClientBuilder<OpenAIClient, OpenAIClientOptions>>? configureClientBuilder = null)
    {
        new OpenAIComponent().AddClient(builder, DefaultConfigSectionName, configureSettings, configureClientBuilder, connectionName, serviceKey: null);
    }

    /// <summary>
    /// Registers <see cref="OpenAIClient"/> as a singleton for given <paramref name="name"/> in the services provided by the <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="name">The name of the component, which is used as the <see cref="ServiceDescriptor.ServiceKey"/> of the service and also to retrieve the connection string from the ConnectionStrings configuration section.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="AzureOpenAISettings"/>. It's invoked after the settings are read from the configuration.</param>
    /// <param name="configureClientBuilder">An optional method that can be used for customizing the <see cref="IAzureClientBuilder{OpenAIClient, OpenAIClientOptions}"/>.</param>
    /// <remarks>Reads the configuration from "Aspire.Azure.AI.OpenAI:{name}" section.</remarks>
    public static void AddKeyedAzureAIOpenAI(
        this IHostApplicationBuilder builder,
        string name,
        Action<AzureOpenAISettings>? configureSettings = null,
        Action<IAzureClientBuilder<OpenAIClient, OpenAIClientOptions>>? configureClientBuilder = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var configurationSectionName = OpenAIComponent.GetKeyedConfigurationSectionName(name, DefaultConfigSectionName);

        new OpenAIComponent().AddClient(builder, configurationSectionName, configureSettings, configureClientBuilder, connectionName: name, serviceKey: name);
    }

    private sealed class OpenAIComponent : AzureComponent<AzureOpenAISettings, OpenAIClient, OpenAIClientOptions>
    {
        // TODO: Remove "Azure.AI.OpenAI" source when https://github.com/Azure/azure-sdk-for-net/issues/39166 is fixed
        protected override string[] ActivitySourceNames => ["Azure.AI.OpenAI.*", "Azure.AI.OpenAI"];

        protected override IAzureClientBuilder<OpenAIClient, OpenAIClientOptions> AddClient<TBuilder>(TBuilder azureFactoryBuilder, AzureOpenAISettings settings, string connectionName, string configurationSectionName)
        {
            return azureFactoryBuilder.RegisterClientFactory<OpenAIClient, OpenAIClientOptions>((options, cred) =>
            {
                var connectionString = settings.ConnectionString;

                if (settings.UseAzureOpenAI)
                {
                    if (string.IsNullOrEmpty(connectionString))
                    {
                        throw new InvalidOperationException($"An OpenAIClient could not be configured. Ensure valid connection information was provided in 'ConnectionStrings:{connectionName}' or specify a 'ConnectionString' in the '{configurationSectionName}' configuration section.");
                    }

                    return new OpenAIClient(new Uri(connectionString), cred, options);
                }
                else
                {
                    if (string.IsNullOrEmpty(settings.OpenAIApiKey))
                    {
                        throw new InvalidOperationException($"An OpenAI API key could not be found. Ensure a valid API key was provided.");
                    }

                    return new OpenAIClient(settings.OpenAIApiKey, options);
                }
            });
        }

        protected override void BindClientOptionsToConfiguration(IAzureClientBuilder<OpenAIClient, OpenAIClientOptions> clientBuilder, IConfiguration configuration)
        {
#pragma warning disable IDE0200 // Remove unnecessary lambda expression - needed so the ConfigBinder Source Generator works
            clientBuilder.ConfigureOptions(options => configuration.Bind(options));
#pragma warning restore IDE0200
        }

        protected override void BindSettingsToConfiguration(AzureOpenAISettings settings, IConfiguration config)
        {
            config.Bind(settings);
        }

        protected override IHealthCheck CreateHealthCheck(OpenAIClient client, AzureOpenAISettings settings)
        {
            throw new NotImplementedException();
        }

        protected override bool GetHealthCheckEnabled(AzureOpenAISettings settings)
        {
            return false;
        }

        protected override TokenCredential? GetTokenCredential(AzureOpenAISettings settings)
            => settings.Credential;

        protected override bool GetTracingEnabled(AzureOpenAISettings settings)
            => settings.Tracing;
    }
}
