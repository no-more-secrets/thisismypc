namespace ThisIsMyPC.Core.Hardware.Lighting;

/// <summary>The private SDK endpoint created for one lighting engine process.</summary>
public sealed record LightingEngineEndpoint(int Port, string AuthenticationToken);
