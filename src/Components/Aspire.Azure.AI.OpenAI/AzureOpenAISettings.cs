// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Data.Common;
using Aspire.Azure.Common;
using Azure;
using Azure.Core;

namespace Aspire.Azure.AI.OpenAI;

/// <summary>
/// The settings relevant to accessing Azure OpenAI or OpenAI.
/// </summary>
public sealed class AzureOpenAISettings : IConnectionStringSettings
{
    private const string AccountEndpoint = "AccountEndpoint";
    private const string AccountKey = "AccountKey";

    /// <summary>
    /// Gets or sets a <see cref="Uri"/> referencing the Azure OpenAI endpoint.
    /// This is likely to be similar to "https://{account_name}.openai.azure.com".
    /// </summary>
    /// <remarks>
    /// Used along with <see cref="Credential"/> or <see cref="Key"/> to establish the connection.
    /// </remarks>
    public Uri? ServiceUri { get; set; }

    /// <summary>
    /// Gets or sets the credential used to authenticate to the Azure OpenAI resource.
    /// </summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>
    /// Gets or sets the key to use to authenticate to the Azure OpenAI endpoint.
    /// </summary>
    /// <remarks>
    /// When defined it will use an <see cref="AzureKeyCredential"/> instance instead of <see cref="Credential"/>.
    /// </remarks>
    public string? Key { get; set; }

    /// <summary>
    /// Gets or sets a boolean value that indicates whether the OpenTelemetry tracing is enabled or not.
    /// </summary>
    /// <value>
    /// The default value is <see langword="true"/>.
    /// </value>
    public bool Tracing { get; set; } = true;

    public void ParseConnectionString(string? connectionString)
    {
        var connectionBuilder = new DbConnectionStringBuilder();
        connectionBuilder.ConnectionString = connectionString;

        if (connectionBuilder.ContainsKey(AccountEndpoint) && Uri.TryCreate(connectionBuilder[AccountEndpoint].ToString(), UriKind.Absolute, out var serviceUri))
        {
            ServiceUri = serviceUri;
        }
        else if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            ServiceUri = uri;
        }

        if (connectionBuilder.ContainsKey(AccountKey))
        {
            Key = connectionBuilder[AccountKey].ToString();
        }
    }
}
