using ECARMF.Kernel.Infrastructure;
using ECARMF.Kernel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ECARMF")
    ?? throw new InvalidOperationException("Connection string 'ECARMF' is not configured.");

builder.Services.AddECARMFInfrastructure(connectionString);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<ECARMFDbContext>().Database.Migrate();
}

app.Run();
