using AtmApp.Api.Application;
using AtmApp.Api.Endpoints;
using AtmApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddDbContext<AtmDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Atm")));

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new MoneyJsonConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AtmExceptionHandler>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DatabaseInitializer.InitializeAsync(
        scope.ServiceProvider.GetRequiredService<AtmDbContext>(),
        scope.ServiceProvider.GetRequiredService<TimeProvider>());
}

app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "AtmApp.Api v1"));
}
else
{
    // Local dev runs on plain HTTP only, so skip the redirect to avoid certificate prompts.
    app.UseHttpsRedirection();
}

app.MapAtmEndpoints();

app.Run();

public partial class Program;
