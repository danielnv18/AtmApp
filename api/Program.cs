using System.Text.Json.Serialization;
using AtmApp.Api.Application;
using AtmApp.Api.Endpoints;
using AtmApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddDbContext<AtmDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Atm")));

// Outside Development, binding failures otherwise end as an empty 400. Throwing routes them
// through AtmExceptionHandler, so every environment answers with problem+json.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // A missing body field is a 400, not a silent default such as Guid.Empty or 0.
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
    options.SerializerOptions.Converters.Add(new MoneyJsonConverter());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AtmExceptionHandler>();

builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DatabaseInitializer.InitializeAsync(
        scope.ServiceProvider.GetRequiredService<AtmDbContext>(),
        scope.ServiceProvider.GetRequiredService<TimeProvider>());
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "AtmApp.Api v1"));
}

app.MapAtmEndpoints();

app.Run();

public partial class Program;
