using System.Reflection;
using BlackListApi.Commands;
using BlackListApi.Data;
using BlackListApi.Infrastructure;
using BlackListApi.Queries;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using NSwag;
using NSwag.Generation.Processors.Security;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateSlimBuilder(args);

// We build the connection string based on the env variables DB_USER, DB_PASSWORD, DB_HOST, DB_PORT, DB_NAME
var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ??
                       builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<EmailsDbContext>(options =>
    options.UseNpgsql(connectionString, o => { o.EnableRetryOnFailure(5); })
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

builder.Services.AddMediator(options =>
    options.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RequestValidationBehavior<,>));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument(config =>
{
    config.DocumentName = "BlackListAPI";
    config.Title = "BlackListAPI v1";
    config.Version = "v1";

    config.AddSecurity("ApiKey", new OpenApiSecurityScheme
    {
        Type = OpenApiSecuritySchemeType.ApiKey,
        Name = "X-Api-Key",
        In = OpenApiSecurityApiKeyLocation.Header,
        Description = "API Key",
        Scheme = "ApiKeyScheme",
    });

    config.OperationProcessors.Add(new OperationSecurityScopeProcessor("ApiKey"));
});

// Add opentelemetry

var otlServiceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "BlackListAPI";

var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(otlServiceName);

builder.Services.AddOpenTelemetry()
    .UseOtlpExporter()
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .SetResourceBuilder(resourceBuilder);
    })
    .WithMetrics(metricProviderBuilder =>
    {
        metricProviderBuilder
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .SetResourceBuilder(resourceBuilder);
    });

// Configure the OpenTelemetry SDK for logs
builder.Logging.ClearProviders();
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.ParseStateValues = true;
    options.IncludeScopes = true;
    options.SetResourceBuilder(resourceBuilder);
});


var app = builder.Build();
app.UseMiddleware<CustomApiKeyAuthenticationMiddleware>();
app.UseCustomExceptionHandler();
app.UseOpenApi();
app.UseSwaggerUi(config =>
{
    config.DocumentTitle = "BlackListAPI";
    config.Path = "/swagger";
    config.DocumentPath = "/swagger/{documentName}/swagger.json";
    config.DocExpansion = "list";
    config.PersistAuthorization = true;
});


// If the Db doesn't exist, create it
using var scope = app.Services.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<EmailsDbContext>();

// Apply the migrations
dbContext.Database.Migrate();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                       ForwardedHeaders.XForwardedProto
});

app.MapPost("/blacklist", async (IMediator mediator, BlackListEmailCommand command, HttpContext httpContext) =>
{
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
    command.SourceIp = ipAddress;

    var response = await mediator.Send(command);
    return Results.Created($"/blacklist/{response.Message}", response);
});

app.MapGet("/blacklist/{email}", async (IMediator mediator, string email) =>
{
    var response = await mediator.Send(new GetEmail { Email = email });
    return Results.Ok(response);
});


app.Run();