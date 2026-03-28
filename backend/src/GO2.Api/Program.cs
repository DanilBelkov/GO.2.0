using System.Text;
using System.Text.Json.Serialization;
using GO2.Api.Application.Auth;
using GO2.Api.Application.Maps;
using GO2.Api.Application.Routes;
using GO2.Api.Application.TerrainTypes;
using GO2.Api.Data;
using GO2.Api.Middleware;
using GO2.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(null, allowIntegerValues: false));
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddProblemDetails(options =>
{
    // Дополняем стандартный ProblemDetails служебными id для диагностики.
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        if (context.HttpContext.Items.TryGetValue(CorrelationIdMiddleware.CorrelationIdKey, out var correlationId))
        {
            context.ProblemDetails.Extensions["correlationId"] = correlationId;
        }
    };
});

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    // Единый формат ответа при ошибках валидации DTO.
    options.InvalidModelStateResponseFactory = context =>
    {
        var details = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Type = "https://httpstatuses.com/400"
        };

        return new BadRequestObjectResult(details);
    };
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Регистрируем прикладные сервисы домена.
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<IOcdImportService, OcdImportService>();
builder.Services.AddScoped<IDigitizationPipelineService, DigitizationPipelineService>();
builder.Services.AddScoped<IDigitizationQualityService, DigitizationQualityService>();
builder.Services.AddScoped<IAuthCommandService, AuthCommandService>();
builder.Services.AddScoped<IMapCommandService, MapCommandService>();
builder.Services.AddScoped<IMapQueryService, MapQueryService>();
builder.Services.AddScoped<ITerrainTypeCommandService, TerrainTypeCommandService>();
builder.Services.AddScoped<ITerrainTypeQueryService, TerrainTypeQueryService>();
builder.Services.AddSingleton<RouteJobStore>();
builder.Services.AddSingleton<RoutingEngineService>();
builder.Services.AddScoped<IRouteCommandService, RouteCommandService>();
builder.Services.AddScoped<IRouteQueryService, RouteQueryService>();

var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"] ?? throw new InvalidOperationException("JWT key is not configured.");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// На старте приложения гарантируем наличие системных типов местности.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await TerrainTypeSeeder.SeedSystemTypesAsync(dbContext, CancellationToken.None);
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler(exceptionApp =>
{
    // Глобальный обработчик исключений с преобразованием в ProblemDetails.
    exceptionApp.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        context.Response.ContentType = "application/problem+json";

        if (exception is UnauthorizedAccessException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Results.Problem(
                title: "Unauthorized",
                detail: exception.Message,
                statusCode: StatusCodes.Status401Unauthorized)
                .ExecuteAsync(context);
            return;
        }

        if (exception is PostgresException postgresException)
        {
            var translated = PostgresErrorTranslator.Translate(postgresException);
            context.Response.StatusCode = translated.StatusCode;
            await Results.Problem(
                title: translated.Title,
                detail: translated.Detail,
                statusCode: translated.StatusCode)
                .ExecuteAsync(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Results.Problem(
            title: "Unexpected server error",
            detail: "Unhandled exception occurred.",
            statusCode: StatusCodes.Status500InternalServerError)
            .ExecuteAsync(context);
    });
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Api v1");
    });
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.UseAuthentication();
app.UseCors("frontend");
app.UseAuthorization();
app.MapControllers();

app.Run();
