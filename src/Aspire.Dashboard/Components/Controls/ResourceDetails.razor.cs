// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Diagnostics;
using Aspire.Dashboard.Components.Controls.PropertyValues;
using Aspire.Dashboard.Components.Pages;
using Aspire.Dashboard.Model;
using Aspire.Dashboard.Telemetry;
using Aspire.Dashboard.Utils;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Aspire.Dashboard.Components.Controls;

public partial class ResourceDetails : IComponentWithTelemetry, IDisposable
{
    [Parameter, EditorRequired]
    public required ResourceViewModel Resource { get; set; }

    [Parameter]
    public required ConcurrentDictionary<string, ResourceViewModel> ResourceByName { get; set; }

    [Parameter]
    public bool ShowSpecOnlyToggle { get; set; }

    [Parameter]
    public bool ShowHiddenResources { get; set; }

    [Inject]
    public required NavigationManager NavigationManager { get; init; }

    [Inject]
    public required IJSRuntime JS { get; init; }

    [Inject]
    public required ComponentTelemetryContextProvider TelemetryContextProvider { get; init; }

    [Inject]
    public required BrowserTimeProvider TimeProvider { get; init; }

    [Inject]
    public required ILogger<ResourceDetails> Logger { get; init; }

    private bool IsSpecOnlyToggleDisabled => !Resource.Environment.All(i => !i.FromSpec) && !GetResourceProperties(ordered: false).Any(static vm => vm.KnownProperty is null);

    // NOTE Excludes URLs as they don't expose sensitive items (and enumerating URLs is non-trivial)
    private IEnumerable<IPropertyGridItem> SensitiveGridItems => Resource.Environment.Cast<IPropertyGridItem>().Concat(_displayedResourcePropertyViewModels).Where(static vm => vm.IsValueSensitive);

    private bool _showAll;
    private ResourceViewModel? _resource;
    private readonly List<DisplayedResourcePropertyViewModel> _displayedResourcePropertyViewModels = new();
    private readonly HashSet<string> _unmaskedItemNames = new();

    private ColumnResizeLabels _resizeLabels = ColumnResizeLabels.Default;
    private ColumnSortLabels _sortLabels = ColumnSortLabels.Default;

    internal IQueryable<EnvironmentVariableViewModel> FilteredEnvironmentVariables =>
        Resource.Environment
            .Where(vm => (_showAll || vm.FromSpec) && ((IPropertyGridItem)vm).MatchesFilter(_filter))
            .AsQueryable();

    internal IQueryable<DisplayedUrl> FilteredUrls =>
        GetUrls()
            .Where(vm => vm.MatchesFilter(_filter))
            .AsQueryable();

    internal IQueryable<ResourceDetailRelationshipViewModel> FilteredRelationships =>
        GetRelationships()
            .Where(vm => vm.MatchesFilter(_filter))
            .AsQueryable();

    internal IQueryable<ResourceDetailRelationshipViewModel> FilteredBackRelationships =>
        GetBackRelationships()
            .Where(vm => vm.MatchesFilter(_filter))
            .AsQueryable();

    internal IQueryable<VolumeViewModel> FilteredVolumes =>
        Resource.Volumes
            .Where(vm => vm.MatchesFilter(_filter))
            .AsQueryable();

    internal IQueryable<HealthReportViewModel> FilteredHealthReports =>
        Resource.HealthReports
            .Where(vm => vm.MatchesFilter(_filter))
            .AsQueryable();

    internal IQueryable<DisplayedResourcePropertyViewModel> FilteredResourceProperties =>
        GetResourceProperties(ordered: true)
            .Where(vm => (_showAll || vm.KnownProperty != null) && vm.MatchesFilter(_filter))
            .AsQueryable();

    private bool _isVolumesExpanded;
    private bool _isEnvironmentVariablesExpanded;
    private bool _isUrlsExpanded;
    private bool _isHealthChecksExpanded;
    private bool _isRelationshipsExpanded;
    private bool _isBackRelationshipsExpanded;

    private string _filter = "";
    private bool? _isMaskAllChecked;
    private bool _dataChanged;
    private Dictionary<string, ComponentMetadata>? _valueComponents;

    private bool IsMaskAllChecked
    {
        get => _isMaskAllChecked ?? false;
        set { _isMaskAllChecked = value; }
    }

    private readonly GridSort<DisplayedUrl> _urlValueSort = GridSort<DisplayedUrl>.ByAscending(vm => vm.Url ?? vm.Text);

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(Resource, _resource))
        {
            // Reset masking and set data changed flag when the resource changes.
            if (!string.Equals(Resource.Name, _resource?.Name, StringComparisons.ResourceName))
            {
                _isMaskAllChecked = true;
                _unmaskedItemNames.Clear();
                _dataChanged = true;
            }

            _resource = Resource;
            _displayedResourcePropertyViewModels.Clear();
            _displayedResourcePropertyViewModels.AddRange(_resource.Properties.Select(p => new DisplayedResourcePropertyViewModel(p.Value, Loc, TimeProvider)));

            // Collapse details sections when they have no data.
            _isUrlsExpanded = GetUrls().Count > 0;
            _isEnvironmentVariablesExpanded = _resource.Environment.Any();
            _isVolumesExpanded = _resource.Volumes.Any();
            _isHealthChecksExpanded = _resource.HealthReports.Any() || _resource.HealthStatus is null; // null means we're waiting for health reports
            _isRelationshipsExpanded = GetRelationships().Any();
            _isBackRelationshipsExpanded = GetBackRelationships().Any();

            foreach (var item in SensitiveGridItems)
            {
                if (_isMaskAllChecked != null)
                {
                    item.IsValueMasked = _isMaskAllChecked.Value;
                }
                else if (_unmaskedItemNames.Count > 0)
                {
                    item.IsValueMasked = !_unmaskedItemNames.Contains(item.Name);
                }
            }

            _valueComponents = new Dictionary<string, ComponentMetadata>
            {
                [KnownProperties.Resource.State] = new ComponentMetadata
                {
                    Type = typeof(ResourceStateValue),
                    Parameters = { ["Resource"] = _resource }
                },
                [KnownProperties.Resource.DisplayName] = new ComponentMetadata
                {
                    Type = typeof(ResourceNameValue),
                    Parameters = { ["Resource"] = _resource, ["FormatName"] = new Func<ResourceViewModel, string>(FormatName) }
                },
                [KnownProperties.Resource.HealthState] = new ComponentMetadata
                {
                    Type = typeof(ResourceHealthStateValue),
                    Parameters = { ["Resource"] = _resource }
                },
            };
        }

        UpdateTelemetryProperties();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_dataChanged)
        {
            if (!firstRender)
            {
                await JS.InvokeVoidAsync("scrollToTop", ".property-grid-container");
            }

            _dataChanged = false;
        }
    }

    protected override void OnInitialized()
    {
        TelemetryContextProvider.Initialize(TelemetryContext);
        (_resizeLabels, _sortLabels) = DashboardUIHelpers.CreateGridLabels(ControlStringsLoc);
    }

    private IEnumerable<ResourceDetailRelationshipViewModel> GetRelationships()
    {
        if (ResourceByName == null)
        {
            return [];
        }

        var items = new List<ResourceDetailRelationshipViewModel>();

        foreach (var resourceRelationships in Resource.Relationships.GroupBy(r => r.ResourceName, StringComparers.ResourceName))
        {
            var matches = ResourceByName.Values
                .Where(r => string.Equals(r.DisplayName, resourceRelationships.Key, StringComparisons.ResourceName))
                .Where(r => !r.IsResourceHidden(ShowHiddenResources))
                .ToList();

            foreach (var match in matches)
            {
                items.Add(ResourceDetailRelationshipViewModel.Create(match, ResourceViewModel.GetResourceName(match, ResourceByName), resourceRelationships));
            }
        }

        return items.OrderBy(r => r.ResourceName, StringComparers.ResourceName);
    }

    private IEnumerable<ResourceDetailRelationshipViewModel> GetBackRelationships()
    {
        if (ResourceByName == null)
        {
            return [];
        }

        var items = new List<ResourceDetailRelationshipViewModel>();

        var otherResources = ResourceByName.Values
            .Where(r => r != Resource)
            .Where(r => !r.IsResourceHidden(ShowHiddenResources));

        foreach (var otherResource in otherResources)
        {
            foreach (var resourceRelationships in otherResource.Relationships.GroupBy(r => r.ResourceName, StringComparers.ResourceName))
            {
                if (string.Equals(resourceRelationships.Key, Resource.DisplayName, StringComparisons.ResourceName))
                {
                    items.Add(ResourceDetailRelationshipViewModel.Create(otherResource, ResourceViewModel.GetResourceName(otherResource, ResourceByName), resourceRelationships));
                }
            }
        }

        return items.OrderBy(r => r.ResourceName, StringComparers.ResourceName);
    }

    private List<DisplayedUrl> GetUrls()
    {
        return ResourceUrlHelpers.GetUrls(Resource, includeInternalUrls: true, includeNonEndpointUrls: true);
    }

    private IEnumerable<DisplayedResourcePropertyViewModel> GetResourceProperties(bool ordered)
    {
        var vms = _displayedResourcePropertyViewModels
            .Where(vm => vm.Value is { HasNullValue: false } and not { KindCase: Value.KindOneofCase.ListValue, ListValue.Values.Count: 0 });

        return ordered
            ? vms.OrderBy(vm => vm.Priority).ThenBy(vm => vm.DisplayName)
            : vms;
    }

    private void OnMaskAllCheckedChanged()
    {
        Debug.Assert(_isMaskAllChecked != null);

        _unmaskedItemNames.Clear();

        foreach (var vm in SensitiveGridItems)
        {
            vm.IsValueMasked = _isMaskAllChecked.Value;
        }
    }

    private void OnValueMaskedChanged(IPropertyGridItem vm)
    {
        // Check the "Mask All" checkbox if all sensitive values are masked.
        var valueMaskedValues = SensitiveGridItems.Select(i => i.IsValueMasked).Distinct().ToList();
        if (valueMaskedValues.Count == 1)
        {
            _isMaskAllChecked = valueMaskedValues[0];
            _unmaskedItemNames.Clear();
        }
        else
        {
            _isMaskAllChecked = null;

            if (vm.IsValueMasked)
            {
                _unmaskedItemNames.Remove(vm.Name);
            }
            else
            {
                _unmaskedItemNames.Add(vm.Name);
            }
        }
    }

    private string FormatName(ResourceViewModel resource)
    {
        return ResourceViewModel.GetResourceName(resource, ResourceByName);
    }

    public Task OnViewRelationshipAsync(ResourceDetailRelationshipViewModel relationship)
    {
        NavigationManager.NavigateTo(DashboardUrls.ResourcesUrl(resource: relationship.Resource.Name));
        return Task.CompletedTask;
    }

    // IComponentWithTelemetry impl
    public ComponentTelemetryContext TelemetryContext { get; } = new(ComponentType.Control, TelemetryComponentIds.ResourceDetails);

    public void UpdateTelemetryProperties()
    {
        TelemetryContext.UpdateTelemetryProperties([
            new ComponentTelemetryProperty(TelemetryPropertyKeys.ResourceType, new AspireTelemetryProperty(TelemetryPropertyValues.GetResourceTypeTelemetryValue(Resource.ResourceType, Resource.SupportsDetailedTelemetry))),
        ], Logger);
    }

    public void Dispose()
    {
        TelemetryContext.Dispose();
    }
}
