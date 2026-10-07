using System.Text.Json;
using System.Text.Json.Serialization;
using BookStore.Api.Common;
using BookStore.Api.Configuration;
using BookStore.Api.Filters;
using BookStore.Api.Middleware;
using BookStore.Application;
using BookStore.Application.Common.Abstractions;
using BookStore.Infrastructure;
using Serilog;
using Serilog.Events;

// Bootstrap logger: captures failures that happen before the host is built.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "BookStore.Api")
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .WriteTo.Console());

    builder.Services
        .AddControllers(options => options.Filters.Add<ValidationFilter>())
        .AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions));

    // Responses written outside MVC (status-code pages, the exception middleware)
    // go through this options object, so both are configured identically.
    builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(
        options => ConfigureJson(options.SerializerOptions));

    builder.Services.AddEnvelopeModelValidation();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();

    // Backs the Identity token providers used for email confirmation and password
    // reset. Registered here because where the key ring lives is a hosting decision.
    builder.Services.AddDataProtection();

    builder.Services.AddApplication(builder.Configuration);
    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services.AddPlatformAuthentication();
    builder.Services.AddPlatformAuthorization(builder.Environment.IsDevelopment());

    builder.Services.AddSwagger();
    builder.Services.AddFrontendCors(builder.Configuration);
    builder.Services.AddApiRateLimiting(builder.Configuration);
    builder.Services.AddProblemDetails();
    builder.Services.AddHealthChecks();

    var app = builder.Build();

    // Bring the schema up to date on every start, in every environment, then put in
    // place the roles, the platform settings and the first administrator (from
    // Admin:Email and Admin:Password). Set Database:AutoMigrate to false to migrate
    // through a pipeline instead.
    var applyMigrations = builder.Configuration.GetValue("Database:AutoMigrate", true);
    var seedData = builder.Configuration.GetValue("Seed:Enabled", true);

    if (applyMigrations || seedData)
    {
        await app.Services.InitialiseDatabaseAsync(applyMigrations, seedData);
    }

    // The envelope middleware must sit outermost so it can catch everything below it.
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    app.UseStatusCodeEnvelope();

    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "BookStore API v1");
            options.DocumentTitle = "BookStore Marketplace API";
        });
    }
    else
    {
        // Only outside Development: the Angular dev server proxies over plain HTTP, and
        // a redirect to the HTTPS port arrives at the browser as a cross-origin request
        // it will not follow, which is every call from the site failing.
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    // Serves the Angular build in wwwroot (index.html for "/") and the uploaded book
    // photographs. Covers are already public information: a cover is what the catalogue
    // shows, and the file names are random rather than guessable.
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.UseCors(CorsSetup.PolicyName);
    app.UseRateLimiter();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health");

    // Angular routes (e.g. /books/5) exist only in the browser, so any other path gets
    // index.html and the client router takes over. Unknown API paths stay a real 404
    // instead of returning the page with a 200.
    app.MapFallback("/api/{**path}", () => Results.NotFound());
    app.MapFallbackToFile("index.html");

    app.Run();
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    Log.Fatal(exception, "BookStore API terminated unexpectedly.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

static void ConfigureJson(JsonSerializerOptions options)
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
}

/// <summary>Exposed so integration tests can boot the API with WebApplicationFactory.</summary>
public partial class Program;
