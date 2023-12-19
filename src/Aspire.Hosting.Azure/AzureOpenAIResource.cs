// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Represents an Azure OpenAI resource.
/// </summary>
/// <param name="name">The name of the resource.</param>
public class AzureOpenAIResource(string name) : Resource(name), IAzureResource, IResourceWithConnectionString
{
    /// <summary>
    /// Gets or sets the connection string for the Azure OpenAI resource.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets the connection string for the Azure OpenAI resource.
    /// </summary>
    /// <returns>The connection string for the Azure OpenAI resource.</returns>
    public string? GetConnectionString() => ConnectionString;
}
