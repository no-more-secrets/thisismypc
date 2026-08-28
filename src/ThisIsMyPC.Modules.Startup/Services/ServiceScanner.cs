using System.Text.RegularExpressions;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Services;

/// <summary>
/// Enumerates all Windows services and marks per-user service template
/// instances (name = template + "_" + hex LUID suffix, e.g. CDPUserSvc_5f3a2)
/// so the UI can group them with their parent template service.
/// </summary>
public sealed partial class ServiceScanner
{
    private readonly IServiceControlService _serviceControl;

    [GeneratedRegex("^(?<template>.+)_(?<suffix>[0-9a-fA-F]{4,8})$")]
    private static partial Regex PerUserInstancePattern();

    public ServiceScanner(IServiceControlService serviceControl)
    {
        _serviceControl = serviceControl;
    }

    /// <summary>Non-null after Scan() when service enumeration itself failed (list is then empty).</summary>
    public string? LastScanError { get; private set; }

    public IReadOnlyList<ServiceEntry> Scan()
    {
        LastScanError = null;
        var enumerated = _serviceControl.EnumerateAll();
        if (!enumerated.IsSuccess || enumerated.Value is null)
        {
            LastScanError = enumerated.ErrorMessage ?? "Service enumeration failed.";
            return [];
        }

        var names = new HashSet<string>(enumerated.Value.Select(s => s.ServiceName), StringComparer.OrdinalIgnoreCase);

        var entries = new List<ServiceEntry>(enumerated.Value.Count);
        foreach (var info in enumerated.Value)
        {
            string? template = null;
            var match = PerUserInstancePattern().Match(info.ServiceName);
            if (match.Success)
            {
                var candidate = match.Groups["template"].Value;
                // Only a real per-user instance when the template service itself exists
                if (names.Contains(candidate))
                    template = candidate;
            }

            entries.Add(new ServiceEntry
            {
                ServiceName = info.ServiceName,
                DisplayName = info.DisplayName,
                Description = info.Description,
                State = info.State,
                StartType = info.StartType,
                IsPerUserInstance = template is not null,
                TemplateServiceName = template,
            });
        }

        return entries;
    }
}
