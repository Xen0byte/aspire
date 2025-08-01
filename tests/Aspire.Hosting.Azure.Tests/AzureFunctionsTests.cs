// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Nodes;
using Aspire.TestUtilities;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Tests.Utils;
using Aspire.Hosting.Utils;
using Azure.Provisioning.Storage;
using Microsoft.Extensions.DependencyInjection;
using static Aspire.Hosting.Utils.AzureManifestUtils;

namespace Aspire.Hosting.Azure.Tests;

public class AzureFunctionsTests
{
    [Fact]
    public async Task AddAzureFunctionsProject_Works()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var funcApp = builder.AddAzureFunctionsProject<TestProject>("funcapp");

        var app = builder.Build();

        await ExecuteBeforeStartHooksAsync(app, default);

        // Assert that default storage resource is configured
        Assert.Contains(builder.Resources, resource =>
            resource is AzureStorageResource && resource.Name.StartsWith(AzureFunctionsProjectResourceExtensions.DefaultAzureFunctionsHostStorageName));
        // Assert that custom project resource type is configured
        Assert.Contains(builder.Resources, resource =>
            resource is AzureFunctionsProjectResource && resource.Name == "funcapp");

        var storage = Assert.Single(builder.Resources.OfType<AzureStorageResource>());
        Assert.True(funcApp.Resource.TryGetAnnotationsOfType<ResourceRelationshipAnnotation>(out var relAnnotations));

        var rel = Assert.Single(relAnnotations);
        Assert.Equal("Reference", rel.Type);
        Assert.Equal(storage, rel.Resource);
    }

    [Fact]
    public async Task AddAzureFunctionsProject_WiresUpHttpEndpointCorrectly_WhenPortArgumentIsProvided()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        builder.AddAzureFunctionsProject<TestProject>("funcapp");

        // Assert that the EndpointAnnotation is configured correctly
        var functionsResource = Assert.Single(builder.Resources.OfType<AzureFunctionsProjectResource>());
        Assert.True(functionsResource.TryGetLastAnnotation<EndpointAnnotation>(out var endpointAnnotation));
        Assert.Equal(7071, endpointAnnotation.Port);
        Assert.Equal(7071, endpointAnnotation.TargetPort);
        Assert.False(endpointAnnotation.IsProxied);

        // Check that no `--port` is present in the generated argument
        // list if it's already defined in the launch profile
        using var app = builder.Build();
        var args = await ArgumentEvaluator.GetArgumentListAsync(functionsResource);

        Assert.Empty(args);
    }

    [Fact]
    public async Task AddAzureFunctionsProject_WiresUpHttpEndpointCorrectly_WhenPortArgumentIsNotProvided()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        builder.AddAzureFunctionsProject<TestProjectWithoutPortArgument>("funcapp")
            // Explicit set endpoint values for assertions later
            .WithEndpoint("http", e =>
            {
                e.UriScheme = "http";
                e.AllocatedEndpoint = new(e, "localhost", 1234);
                e.TargetPort = 9876;
            });

        // Assert that the EndpointAnnotation is configured correctly
        var functionsResource = Assert.Single(builder.Resources.OfType<AzureFunctionsProjectResource>());
        Assert.True(functionsResource.TryGetLastAnnotation<EndpointAnnotation>(out var endpointAnnotation));
        Assert.Null(endpointAnnotation.Port);
        Assert.Equal(9876, endpointAnnotation.TargetPort);
        Assert.True(endpointAnnotation.IsProxied);

        // Check that `--port` is present in the args
        using var app = builder.Build();
        var args = await ArgumentEvaluator.GetArgumentListAsync(functionsResource);

        Assert.Collection(args,
            arg => Assert.Equal("--port", arg),
            arg => Assert.Equal("9876", arg)
        );
    }

    [Fact]
    public void AddAzureFunctionsProject_WiresUpHttpEndpointCorrectly_WhenMultiplePortArgumentsProvided()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        builder.AddAzureFunctionsProject<TestProjectWithMultiplePorts>("funcapp");

        // Assert that the EndpointAnnotation is configured correctly
        var functionsResource = Assert.Single(builder.Resources.OfType<AzureFunctionsProjectResource>());
        Assert.True(functionsResource.TryGetLastAnnotation<EndpointAnnotation>(out var endpointAnnotation));
        Assert.Equal(7072, endpointAnnotation.Port);
        Assert.Equal(7072, endpointAnnotation.TargetPort);
        Assert.False(endpointAnnotation.IsProxied);
    }

    [Fact]
    public void AddAzureFunctionsProject_WiresUpHttpEndpointCorrectly_WhenPortArgumentIsMalformed()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        builder.AddAzureFunctionsProject<TestProjectWithMalformedPort>("funcapp");

        // Assert that the EndpointAnnotation is configured correctly
        var functionsResource = Assert.Single(builder.Resources.OfType<AzureFunctionsProjectResource>());
        Assert.True(functionsResource.TryGetLastAnnotation<EndpointAnnotation>(out var endpointAnnotation));
        Assert.Null(endpointAnnotation.Port);
        Assert.Null(endpointAnnotation.TargetPort);
        Assert.True(endpointAnnotation.IsProxied);
    }

    [Fact]
    public void AddAzureFunctionsProject_WiresUpHttpEndpointCorrectly_WhenPortArgumentIsPartial()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        builder.AddAzureFunctionsProject<TestProjectWithPartialPort>("funcapp");

        // Assert that the EndpointAnnotation is configured correctly
        var functionsResource = Assert.Single(builder.Resources.OfType<AzureFunctionsProjectResource>());
        Assert.True(functionsResource.TryGetLastAnnotation<EndpointAnnotation>(out var endpointAnnotation));
        Assert.Null(endpointAnnotation.Port);
        Assert.Null(endpointAnnotation.TargetPort);
        Assert.True(endpointAnnotation.IsProxied);
    }

    [Fact]
    public void AddAzureFunctionsProject_GeneratesUniqueDefaultHostStorageResourceName()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        builder.AddAzureFunctionsProject<TestProjectWithMalformedPort>("funcapp");

        // Assert that the default storage resource is unique
        var storageResources = Assert.Single(builder.Resources.OfType<AzureStorageResource>());
        Assert.NotEqual(AzureFunctionsProjectResourceExtensions.DefaultAzureFunctionsHostStorageName, storageResources.Name);
        Assert.StartsWith(AzureFunctionsProjectResourceExtensions.DefaultAzureFunctionsHostStorageName, storageResources.Name);
    }

    [Fact]
    [RequiresDocker]
    public async Task AddAzureFunctionsProject_RemoveDefaultHostStorageWhenUseHostStorageIsUsed()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var storage = builder.AddAzureStorage("my-own-storage").RunAsEmulator();
        var funcApp = builder.AddAzureFunctionsProject<TestProjectWithMalformedPort>("funcapp")
            .WithHostStorage(storage);

        using var host = builder.Build();
        await host.StartAsync();

        // Assert that the default storage resource is not present
        var model = host.Services.GetRequiredService<DistributedApplicationModel>();
        Assert.DoesNotContain(model.Resources.OfType<AzureStorageResource>(),
            r => r.Name.StartsWith(AzureFunctionsProjectResourceExtensions.DefaultAzureFunctionsHostStorageName));
        var storageResource = Assert.Single(model.Resources.OfType<AzureStorageResource>());
        Assert.Equal("my-own-storage", storageResource.Name);

        Assert.True(funcApp.Resource.TryGetAnnotationsOfType<ResourceRelationshipAnnotation>(out var relAnnotations));
        var rel = Assert.Single(relAnnotations);
        Assert.Equal("Reference", rel.Type);
        Assert.Equal(storage.Resource, rel.Resource);

        await host.StopAsync();
    }

    [Fact]
    [RequiresDocker]
    public async Task AddAzureFunctionsProject_WorksWithMultipleProjects()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        builder.AddAzureFunctionsProject<TestProject>("funcapp");
        builder.AddAzureFunctionsProject<TestProject>("funcapp2");

        using var host = builder.Build();
        await host.StartAsync();

        // Assert that the default storage resource is not present
        var model = host.Services.GetRequiredService<DistributedApplicationModel>();
        Assert.Single(model.Resources.OfType<AzureStorageResource>(),
            r => r.Name.StartsWith(AzureFunctionsProjectResourceExtensions.DefaultAzureFunctionsHostStorageName));

        await host.StopAsync();
    }

    [Fact]
    public void AddAzureFunctionsProject_UsesCorrectNameUnderPublish()
    {
        using var builder = TestDistributedApplicationBuilder.Create(DistributedApplicationOperation.Publish);
        builder.AddAzureFunctionsProject<TestProject>("funcapp");

        var resource = Assert.Single(builder.Resources.OfType<AzureStorageResource>());

        Assert.NotEqual(AzureFunctionsProjectResourceExtensions.DefaultAzureFunctionsHostStorageName, resource.Name);
        Assert.StartsWith(AzureFunctionsProjectResourceExtensions.DefaultAzureFunctionsHostStorageName, resource.Name);
    }

    [Fact]
    public void AddAzureFunctionsProject_WiresUpHttpsEndpointCorrectly_WhenUseHttpsArgumentIsProvided()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        builder.AddAzureFunctionsProject<TestProjectWithHttps>("funcapp");

        // Assert that the EndpointAnnotation is configured correctly
        var functionsResource = Assert.Single(builder.Resources.OfType<AzureFunctionsProjectResource>());
        Assert.True(functionsResource.TryGetLastAnnotation<EndpointAnnotation>(out var endpointAnnotation));
        Assert.Equal(7071, endpointAnnotation.Port);
        Assert.Equal(7071, endpointAnnotation.TargetPort);
        Assert.False(endpointAnnotation.IsProxied);
        Assert.Equal("https", endpointAnnotation.UriScheme);
    }

    [Fact]
    public async Task AddAzureFunctionsProject_ConfiguresEnvironmentVariables_WhenInPublishMode()
    {
        using var builder = TestDistributedApplicationBuilder.Create(DistributedApplicationOperation.Publish);
        builder.AddAzureFunctionsProject<TestProject>("funcapp");

        var functionsResource = Assert.Single(builder.Resources.OfType<AzureFunctionsProjectResource>());
        Assert.True(functionsResource.TryGetAnnotationsOfType<EnvironmentCallbackAnnotation>(out var envAnnotations));

        var context = new EnvironmentCallbackContext(builder.ExecutionContext);
        foreach (var envAnnotation in envAnnotations)
        {
            await envAnnotation.Callback(context);
        }

        // Verify ASPNETCORE_URLS is set correctly with the target port
        var aspNetCoreUrls = context.EnvironmentVariables["ASPNETCORE_URLS"];
        Assert.NotNull(aspNetCoreUrls);
        var aspNetCoreUrlsValue = await ((ReferenceExpression)aspNetCoreUrls).GetValueAsync(default);
        Assert.Contains("8080", aspNetCoreUrlsValue);
    }

    [Fact]
    public async Task AddAzureFunctionsProject_WiresUpHttpsEndpointCorrectly_WhenOnlyUseHttpsArgumentIsProvided()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        builder.AddAzureFunctionsProject<TestProjectWithHttpsNoPort>("funcapp")
            // Explicit set endpoint values for assertions later
            .WithEndpoint("https", e =>
            {
                e.UriScheme = "https";
                e.AllocatedEndpoint = new(e, "localhost", 1234);
                e.TargetPort = 9876;
            });

        // Assert that the EndpointAnnotation is configured correctly
        var functionsResource = Assert.Single(builder.Resources.OfType<AzureFunctionsProjectResource>());
        Assert.True(functionsResource.TryGetLastAnnotation<EndpointAnnotation>(out var endpointAnnotation));
        Assert.Null(endpointAnnotation.Port);
        Assert.Equal(9876, endpointAnnotation.TargetPort);
        Assert.True(endpointAnnotation.IsProxied);
        Assert.Equal("https", endpointAnnotation.UriScheme);

        // Check that `--port` is present in the args
        using var app = builder.Build();
        var args = await ArgumentEvaluator.GetArgumentListAsync(functionsResource);

        Assert.Collection(args,
            arg => Assert.Equal("--port", arg),
            arg => Assert.Equal("9876", arg)
        );
    }

    [Fact]
    public async Task AddAzureFunctionsProject_CanGetStorageManifestSuccessfully()
    {
        using var builder = TestDistributedApplicationBuilder.Create(DistributedApplicationOperation.Publish);

        // hardcoded sha256 to make the storage name deterministic
        builder.Configuration["AppHost:Sha256"] = "634f8";
        var project = builder.AddAzureFunctionsProject<TestProjectWithHttpsNoPort>("funcapp");

        var app = builder.Build();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        await ExecuteBeforeStartHooksAsync(app, default);

        var storage = Assert.Single(model.Resources.OfType<AzureProvisioningResource>(), r => r.Name == "funcstorage634f8");

        var (storageManifest, _) = await GetManifestWithBicep(storage);

        var expectedRolesManifest =
            """
            {
              "type": "azure.bicep.v0",
              "path": "funcstorage634f8.module.bicep"
            }
            """;
        Assert.Equal(expectedRolesManifest, storageManifest.ToString());
    }

    [Fact]
    public async Task AddAzureFunctionsProject_WorksWithAddAzureContainerAppsInfrastructure()
    {
        using var builder = TestDistributedApplicationBuilder.Create(DistributedApplicationOperation.Publish);
        builder.AddAzureContainerAppEnvironment("env");

        // hardcoded sha256 to make the storage name deterministic
        builder.Configuration["AppHost:Sha256"] = "634f8";
        var funcApp = builder.AddAzureFunctionsProject<TestProjectWithHttpsNoPort>("funcapp");

        var app = builder.Build();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        await ExecuteBeforeStartHooksAsync(app, default);

        var (_, bicep) = await GetManifestWithBicep(funcApp.Resource.GetDeploymentTargetAnnotation()!.DeploymentTarget);

        var projRolesStorage = Assert.Single(model.Resources.OfType<AzureProvisioningResource>(), r => r.Name == "funcapp-roles-funcstorage634f8");

        var (rolesManifest, rolesBicep) = await GetManifestWithBicep(projRolesStorage);

        await Verify(bicep, "bicep")
              .AppendContentAsFile(rolesManifest.ToString(), "json")
              .AppendContentAsFile(rolesBicep, "bicep");
    }

    [Fact]
    public async Task AddAzureFunctionsProject_WorksWithAddAzureContainerAppsInfrastructure_WithHostStorage()
    {
        using var builder = TestDistributedApplicationBuilder.Create(DistributedApplicationOperation.Publish);
        builder.AddAzureContainerAppEnvironment("env");

        // hardcoded sha256 to make the storage name deterministic
        var storage = builder.AddAzureStorage("my-own-storage").RunAsEmulator();
        builder.Configuration["AppHost:Sha256"] = "634f8";
        builder.AddAzureFunctionsProject<TestProjectWithHttpsNoPort>("funcapp")
            .WithHostStorage(storage);

        var app = builder.Build();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        await ExecuteBeforeStartHooksAsync(app, default);

        var projRolesStorage = Assert.Single(model.Resources.OfType<AzureProvisioningResource>(), r => r.Name == "funcapp-roles-my-own-storage");

        var (rolesManifest, rolesBicep) = await GetManifestWithBicep(projRolesStorage);

        await Verify(rolesManifest.ToString(), "json")
              .AppendContentAsFile(rolesBicep, "bicep");
              
    }

    [Fact]
    public async Task AddAzureFunctionsProject_WorksWithAddAzureContainerAppsInfrastructure_WithHostStorage_WithRoleAssignments()
    {
        using var builder = TestDistributedApplicationBuilder.Create(DistributedApplicationOperation.Publish);
        builder.AddAzureContainerAppEnvironment("env");

        // hardcoded sha256 to make the storage name deterministic
        var storage = builder.AddAzureStorage("my-own-storage").RunAsEmulator();
        builder.Configuration["AppHost:Sha256"] = "634f8";
        builder.AddAzureFunctionsProject<TestProjectWithHttpsNoPort>("funcapp")
            .WithHostStorage(storage)
            .WithRoleAssignments(storage, StorageBuiltInRole.StorageBlobDataOwner);

        var app = builder.Build();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        await ExecuteBeforeStartHooksAsync(app, default);

        var projRolesStorage = Assert.Single(model.Resources.OfType<AzureProvisioningResource>(), r => r.Name == "funcapp-roles-my-own-storage");

        var (rolesManifest, rolesBicep) = await GetManifestWithBicep(projRolesStorage);

        await Verify(rolesManifest.ToString(), "json")
              .AppendContentAsFile(rolesBicep, "bicep");
              
    }

    [Fact]
    public async Task MultipleAddAzureFunctionsProject_WorksWithAddAzureContainerAppsInfrastructure_WithHostStorage_WithRoleAssignments()
    {
        using var builder = TestDistributedApplicationBuilder.Create(DistributedApplicationOperation.Publish);
        builder.AddAzureContainerAppEnvironment("env");

        // hardcoded sha256 to make the storage name deterministic
        var storage = builder.AddAzureStorage("my-own-storage").RunAsEmulator();
        builder.Configuration["AppHost:Sha256"] = "634f8";
        builder.AddAzureFunctionsProject<TestProjectWithHttpsNoPort>("funcapp")
            .WithHostStorage(storage)
            .WithRoleAssignments(storage, StorageBuiltInRole.StorageBlobDataOwner);

        builder.AddAzureFunctionsProject<TestProjectWithHttpsNoPort>("funcapp2");

        var app = builder.Build();

        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        await ExecuteBeforeStartHooksAsync(app, default);

        var projRolesStorage = Assert.Single(model.Resources.OfType<AzureProvisioningResource>(), r => r.Name == "funcapp-roles-my-own-storage");
        var projRolesStorage2 = Assert.Single(model.Resources.OfType<AzureProvisioningResource>(), r => r.Name == "funcapp2-roles-funcstorage634f8");

        var (rolesManifest, rolesBicep) = await GetManifestWithBicep(projRolesStorage);
        var (rolesManifest2, rolesBicep2) = await GetManifestWithBicep(projRolesStorage2);

        await Verify(rolesManifest.ToString(), "json")
              .AppendContentAsFile(rolesBicep, "bicep")
              .AppendContentAsFile(rolesManifest2.ToString(), "json")
              .AppendContentAsFile(rolesBicep2, "bicep");
              
    }

    private static Task<(JsonNode ManifestNode, string BicepText)> GetManifestWithBicep(IResource resource) =>
        AzureManifestUtils.GetManifestWithBicep(resource, skipPreparer: true);

    private sealed class TestProject : IProjectMetadata
    {
        public string ProjectPath => "some-path";

        public LaunchSettings LaunchSettings => new()
        {
            Profiles = new Dictionary<string, LaunchProfile>
            {
                ["funcapp"] = new()
                {
                    CommandLineArgs = "--port 7071",
                    LaunchBrowser = false,
                }
            }
        };
    }

    private sealed class TestProjectWithMalformedPort : IProjectMetadata
    {
        public string ProjectPath => "some-path";

        public LaunchSettings LaunchSettings => new()
        {
            Profiles = new Dictionary<string, LaunchProfile>
            {
                ["funcapp"] = new()
                {
                    CommandLineArgs = "--port 70b1",
                    LaunchBrowser = false,
                }
            }
        };
    }

    private sealed class TestProjectWithPartialPort : IProjectMetadata
    {
        public string ProjectPath => "some-path";

        public LaunchSettings LaunchSettings => new()
        {
            Profiles = new Dictionary<string, LaunchProfile>
            {
                ["funcapp"] = new()
                {
                    CommandLineArgs = "--port",
                    LaunchBrowser = false,
                }
            }
        };
    }

    private sealed class TestProjectWithoutPortArgument : IProjectMetadata
    {
        public string ProjectPath => "some-path";

        public LaunchSettings LaunchSettings => new()
        {
            Profiles = new Dictionary<string, LaunchProfile>
            {
                ["funcapp"] = new()
                {
                    LaunchBrowser = false,
                }
            }
        };
    }

    private sealed class TestProjectWithMultiplePorts : IProjectMetadata
    {
        public string ProjectPath => "some-path";

        public LaunchSettings LaunchSettings => new()
        {
            Profiles = new Dictionary<string, LaunchProfile>
            {
                ["funcapp"] = new()
                {
                    CommandLineArgs = "--port 7072 --port 7071",
                    LaunchBrowser = false,
                }
            }
        };
    }

    private sealed class TestProjectWithHttps : IProjectMetadata
    {
        public string ProjectPath => "some-path";

        public LaunchSettings LaunchSettings => new()
        {
            Profiles = new Dictionary<string, LaunchProfile>
            {
                ["funcapp"] = new()
                {
                    CommandLineArgs = "--port 7071 --useHttps",
                    LaunchBrowser = false,
                }
            }
        };
    }

    private sealed class TestProjectWithHttpsNoPort : IProjectMetadata
    {
        public string ProjectPath => "some-path";

        public LaunchSettings LaunchSettings => new()
        {
            Profiles = new Dictionary<string, LaunchProfile>
            {
                ["funcapp"] = new()
                {
                    CommandLineArgs = "--useHttps",
                    LaunchBrowser = false,
                }
            }
        };
    }
}
