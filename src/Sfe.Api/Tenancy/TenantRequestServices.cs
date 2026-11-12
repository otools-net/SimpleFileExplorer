namespace Sfe.Api.Tenancy;

/// <summary>
/// The request's services, with the Core's own (<see cref="TenancyServiceCollectionExtensions.LibraryTypes"/>) taken
/// from the request's library.
/// </summary>
internal sealed class TenantRequestServices(IServiceProvider request, IServiceProvider library)
    : IKeyedServiceProvider,
        ISupportRequiredService
{
    public object? GetService(Type serviceType) =>
        TenancyServiceCollectionExtensions.LibraryTypes.Contains(serviceType)
            ? library.GetService(serviceType)
            : request.GetService(serviceType);

    public object GetRequiredService(Type serviceType) =>
        GetService(serviceType)
        ?? throw new InvalidOperationException($"No service for type '{serviceType}' has been registered.");

    // Keyed services (OpenAPI's documents...) are never a library's.
    public object? GetKeyedService(Type serviceType, object? serviceKey) =>
        ((IKeyedServiceProvider)request).GetKeyedService(serviceType, serviceKey);

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
        ((IKeyedServiceProvider)request).GetRequiredKeyedService(serviceType, serviceKey);
}
