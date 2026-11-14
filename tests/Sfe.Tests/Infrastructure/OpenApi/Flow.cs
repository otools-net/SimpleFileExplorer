namespace Sfe.Tests.Infrastructure.OpenApi;

/// <summary>
/// Which way a value travels: what clients send may accept more later, what they receive may give less, never the
/// other way round.
/// </summary>
[Flags]
public enum Flow
{
    Input = 1,
    Output = 2,
    Both = Input | Output,
}
