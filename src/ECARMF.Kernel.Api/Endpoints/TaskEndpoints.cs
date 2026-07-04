using ECARMF.Kernel.Application.Identity;
using ECARMF.Kernel.Application.Workflow;
using ECARMF.Kernel.Domain.Identity;

namespace ECARMF.Kernel.Api.Endpoints;

public static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tasks", async (
            int? limit, HttpContext context, IUserStore users, ITaskStore tasks, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;
            return Results.Ok(await tasks.GetRecentAsync(tenantId, Math.Clamp(limit ?? 50, 1, 200), ct));
        });

        app.MapPost("/api/tasks/{id:guid}/complete", async (
            Guid id, HttpContext context, IUserStore users, ITaskStore tasks, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, user) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;
            if (user!.IsSystemActor)
                return Results.BadRequest(new { error = "Tasks are human work items; an AI/system actor cannot complete them." });

            var task = await tasks.GetAsync(tenantId, id, ct);
            if (task is null) return Results.NotFound();
            if (task.Status == "Completed") return Results.BadRequest(new { error = "Task is already completed." });

            task.Status = "Completed";
            task.CompletedBy = user.Identifier;
            task.CompletedAt = DateTimeOffset.UtcNow;
            await tasks.UpdateAsync(task, ct);
            return Results.Ok(task);
        });

        app.MapGet("/api/notifications", async (
            int? limit, HttpContext context, IUserStore users, INotificationStore notifications, CancellationToken ct) =>
        {
            if (!TenantResolution.TryGetTenant(context, out var tenantId))
                return TenantResolution.MissingTenantResult();
            var (error, _) = await AccessGuard.RequireAsync(context, users, tenantId, Permissions.RecordRead, ct);
            if (error is not null) return error;
            return Results.Ok(await notifications.GetRecentAsync(tenantId, Math.Clamp(limit ?? 50, 1, 200), ct));
        });

        return app;
    }
}
