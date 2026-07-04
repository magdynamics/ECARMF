using ECARMF.Kernel.Application.Registries;

namespace ECARMF.Kernel.Api.Endpoints;

public static class RegistryEndpoints
{
    public static IEndpointRouteBuilder MapRegistryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/registries");

        group.MapGet("/entities", (IEntityRegistry registry) =>
            Results.Ok(registry.GetAll()));

        group.MapGet("/rules", (IRuleRegistry registry) =>
            Results.Ok(registry.GetAll()));

        group.MapGet("/events", (IEventRegistry registry) =>
            Results.Ok(registry.GetAll()));

        group.MapGet("/capabilities", (ICapabilityRegistry registry) =>
            Results.Ok(registry.GetAll()));

        return app;
    }
}
