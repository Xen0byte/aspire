// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Components.ConformanceTests;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aspire.Azure.AI.OpenAI.Tests;

public class ConformanceTests : ConformanceTests<OpenAIClient, AzureOpenAISettings>
{
    // Fake connection string for cases when credentials are unavailable and need to switch to raw connection string
    protected const string ServiceUri = "https://aspireopenaitests.openai.azure.com/";
    protected const string Key = "fake";

    protected override ServiceLifetime ServiceLifetime => ServiceLifetime.Singleton;

    protected override string[] RequiredLogCategories => new string[] {
        "Azure.Core",
        // Not triggered because no connection is made
        // "Azure.AI.OpenAI",
    };

    protected override bool SupportsKeyedRegistrations => true;

    protected override string JsonSchemaPath => "src/Components/Aspire.Azure.AI.OpenAI/ConfigurationSchema.json";

    protected override string ValidJsonConfig => """
        {
          "Aspire": {
            "Azure": {
              "AI": {
                "OpenAI": {
                  "ServiceUri": "http://YOUR_URI",
                  "Tracing": true,
                  "ClientOptions": {
                    "ConnectionIdleTimeout": "PT1S",
                    "EnableCrossEntityTransactions": true,
                    "RetryOptions": {
                      "Mode": "Fixed",
                      "MaxDelay": "PT3S"  
                    },
                    "TransportType": "AmqpWebSockets"
                  }
                }
              }
            }
          }
        }
        """;

    protected override (string json, string error)[] InvalidJsonToErrorMessage => new[]
        {
            ("""{"Aspire": { "Azure": { "AI":{ "OpenAI": {"ServiceUri": "YOUR_URI"}}}}}""", "Value does not match format \"uri\""),
            ("""{"Aspire": { "Azure": { "AI":{ "OpenAI": {"ServiceUri": "http://YOUR_URI", "Tracing": "false"}}}}}""", "Value is \"string\" but should be \"boolean\""),
        };

    protected override string ActivitySourceName => throw new NotImplementedException();

    // When credentials are not available, we switch to using raw connection string (otherwise we get CredentialUnavailableException)
    protected KeyValuePair<string, string?> GetMainConfigEntry(string? key)
        => CanConnectToServer
                ? new(CreateConfigKey("Aspire:Azure:AI:OpenAI", key, nameof(AzureOpenAISettings.ServiceUri)), ServiceUri)
                : new(CreateConfigKey("Aspire:Azure:AI:OpenAI", key, nameof(AzureOpenAISettings.Key)), Key);

    protected override void PopulateConfiguration(ConfigurationManager configuration, string? key = null)
        => configuration.AddInMemoryCollection(new KeyValuePair<string, string?>[]
        {
            new(CreateConfigKey("Aspire:Azure:AI:OpenAI", key, "ServiceUri"), ServiceUri)
        });

    protected override void RegisterComponent(HostApplicationBuilder builder, Action<AzureOpenAISettings>? configure = null, string? key = null)
    {
        if (key is null)
        {
            builder.AddAzureAIOpenAI("openai", ConfigureCredentials);
        }
        else
        {
            builder.AddKeyedAzureAIOpenAI(key, ConfigureCredentials);
        }

        void ConfigureCredentials(AzureOpenAISettings settings)
        {
            if (CanConnectToServer)
            {
                settings.Credential = new DefaultAzureCredential();
            }

            configure?.Invoke(settings);
        }
    }

    protected override void SetHealthCheck(AzureOpenAISettings options, bool enabled)
        => throw new NotImplementedException();

    protected override void SetMetrics(AzureOpenAISettings options, bool enabled)
        => throw new NotImplementedException();

    protected override void SetTracing(AzureOpenAISettings options, bool enabled)
        => options.Tracing = enabled;

    protected override void TriggerActivity(OpenAIClient service)
        => service.GetCompletions(new CompletionsOptions { DeploymentName = "dummy-gpt" });
}
