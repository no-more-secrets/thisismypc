namespace ThisIsMyPC.Core.Modules;

public record ModuleAvailability(bool IsAvailable, string? Reason = null, string? RemediationHint = null);
