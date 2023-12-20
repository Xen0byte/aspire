// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Aspire.Azure.Messaging.ServiceBus.Tests;

public class AspireServiceBusExtensionsTests
{
    private const string ConnectionString = "AccountEndpoint=https://aspireopenaitests.openai.azure.com/;AccountKey=fake";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadsFromConnectionStringsCorrectly(bool useKeyed)
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration.AddInMemoryCollection([
            new KeyValuePair<string, string?>("ConnectionStrings:openai", ConnectionString)
        ]);

        if (useKeyed)
        {
            builder.AddKeyedAzureAIOpenAI("openai");
        }
        else
        {
            builder.AddAzureAIOpenAI("openai");
        }

        var host = builder.Build();
        var client = useKeyed ?
            host.Services.GetRequiredKeyedService<OpenAIClient>("openai") :
            host.Services.GetRequiredService<OpenAIClient>();

        Assert.NotNull(client);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConnectionStringCanBeSetInCode(bool useKeyed)
    {
        var uri = new Uri("https://aspireopenaitests.openai.azure.com/");
        var key = "fake";
        var builder = Host.CreateEmptyApplicationBuilder(null);

        if (useKeyed)
        {
            builder.AddKeyedAzureAIOpenAI("openai", settings => { settings.ServiceUri = uri; settings.Key = key; });
        }
        else
        {
            builder.AddAzureAIOpenAI("openai", settings => { settings.ServiceUri = uri; settings.Key = key; });
        }

        var host = builder.Build();
        var client = useKeyed ?
            host.Services.GetRequiredKeyedService<OpenAIClient>("openai") :
            host.Services.GetRequiredService<OpenAIClient>();

        Assert.NotNull(client);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConnectionNameWinsOverConfigSection(bool useKeyed)
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);

        var key = useKeyed ? "openai" : null;
        builder.Configuration.AddInMemoryCollection([
            new KeyValuePair<string, string?>(ConformanceTests.CreateConfigKey("Aspire:Azure:AI.OpenAI", key, "ConnectionString"), "unused"),
            new KeyValuePair<string, string?>("ConnectionStrings:openai", ConnectionString)
        ]);

        if (useKeyed)
        {
            builder.AddKeyedAzureAIOpenAI("openai");
        }
        else
        {
            builder.AddAzureAIOpenAI("openai");
        }

        var host = builder.Build();
        var client = useKeyed ?
            host.Services.GetRequiredKeyedService<OpenAIClient>("openai") :
            host.Services.GetRequiredService<OpenAIClient>();

        Assert.NotNull(client);
    }
}
