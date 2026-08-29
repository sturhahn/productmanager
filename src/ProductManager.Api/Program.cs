using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ProductManager.Api.Auth;
using ProductManager.Api.Infrastructure;
using ProductManager.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ---------- Persistence ----------
// The "Testing" environment leaves the provider unregistered so the integration tests can plug in
// SQLite; every other environment is PostgreSQL.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
}

// ---------- Auth (single-user JWT) ----------
builder.Services
    .AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<TokenIssuer>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Configured from AuthOptions rather than raw configuration so the token issuer and the token
// validator can never disagree about the signing key.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthOptions>>((jwt, authOptions) =>
    {
        var auth = authOptions.Value;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = auth.Issuer,
            ValidateAudience = true,
            ValidAudience = auth.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

// Every endpoint requires the token unless it opts out with [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// ---------- MVC / errors ----------
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums travel as their names, so the OpenAPI contract stays readable for the CLI and the app.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// ---------- OpenAPI ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ProductManager API",
        Version = "v1",
        Description = "Roadmap & market-simulation system. The REST API is the only write path: " +
                      "the web app, the CLI and the MCP server are all clients of this contract."
    });

    options.AddSecurityDefinition("bearerAuth", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Token from POST /api/auth/token."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "bearerAuth" }
            },
            Array.Empty<string>()
        }
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory, "ProductManager.Api.xml");
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);

    options.SupportNonNullableReferenceTypes();
    options.CustomSchemaIds(OpenApiSchemaIds.For);
});

// ---------- CORS (the Next.js app runs on its own origin) ----------
const string FrontendCors = "frontend";
builder.Services.AddCors(options => options.AddPolicy(FrontendCors, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                 ?? ["http://localhost:3000"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddHostedService<DatabaseInitializer>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// The OpenAPI document is served at a stable route in every environment so the CLI and the
// frontend can regenerate their client from a running API; the UI is Development-only.
app.UseSwagger(options => options.RouteTemplate = "swagger/{documentName}/swagger.json");
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ProductManager API v1");
        options.DocumentTitle = "ProductManager API";
    });
}

app.UseCors(FrontendCors);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>Entry point exposed for <c>WebApplicationFactory</c> in the integration tests.</summary>
public partial class Program;
