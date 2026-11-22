using AppDeCaixa.Api;
using AppDeCaixa.Api.Authentication;
using AppDeCaixa.Api.CashSessions;
using AppDeCaixa.Api.Collaborators;
using AppDeCaixa.Api.Commissions;
using AppDeCaixa.Api.ServiceOrders;
using AppDeCaixa.Api.Payments;
using AppDeCaixa.Api.Services;
using AppDeCaixa.Api.Users;
using AppDeCaixa.Application.Authentication;
using AppDeCaixa.Application.CashSessions;
using AppDeCaixa.Application.Collaborators;
using AppDeCaixa.Application.Commissions;
using AppDeCaixa.Application.ServiceOrders;
using AppDeCaixa.Application.Payments;
using AppDeCaixa.Application.Services;
using AppDeCaixa.Infrastructure.Authentication;
using AppDeCaixa.Infrastructure.CashSessions;
using AppDeCaixa.Infrastructure.Collaborators;
using AppDeCaixa.Infrastructure.Commissions;
using AppDeCaixa.Infrastructure.ServiceOrders;
using AppDeCaixa.Infrastructure.Payments;
using AppDeCaixa.Infrastructure.Services;
using AppDeCaixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler(options =>
{
    options.ExceptionHandler = async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "An unexpected error occurred.").ExecuteAsync(context);
    };
});

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the raw access token. Swagger will send it as a Bearer token."
    });
    options.OperationFilter<AuthorizeOperationFilter>();
});
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:3000")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=organizaeh;Username=appdecaixa;Password=development-only";
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
var businessTimeZoneId = builder.Configuration["Business:TimeZoneId"] ?? "America/Bahia";
builder.Services.AddSingleton(new BusinessDateProvider(TimeZoneInfo.FindSystemTimeZoneById(businessTimeZoneId)));

var configuredSigningKey = builder.Configuration["Authentication:SigningKey"];
byte[] signingKeyBytes;
if (string.IsNullOrWhiteSpace(configuredSigningKey))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Authentication:SigningKey must be configured outside Development.");
    }

    signingKeyBytes = RandomNumberGenerator.GetBytes(64);
}
else
{
    signingKeyBytes = Encoding.UTF8.GetBytes(configuredSigningKey);
    if (signingKeyBytes.Length < 32)
    {
        throw new InvalidOperationException("Authentication:SigningKey must be at least 32 bytes.");
    }
}

var configuredBootstrapKey = builder.Configuration["Bootstrap:AdminKey"];
if (!string.IsNullOrWhiteSpace(configuredBootstrapKey) && Encoding.UTF8.GetByteCount(configuredBootstrapKey) < 32)
{
    throw new InvalidOperationException("Bootstrap:AdminKey must be at least 32 bytes when configured.");
}

var signingKey = new SymmetricSecurityKey(signingKeyBytes);
builder.Services.AddSingleton(new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
builder.Services.Configure<JwtSettings>(settings =>
{
    settings.Issuer = builder.Configuration["Authentication:Issuer"] ?? "AppDeCaixa";
    settings.Audience = builder.Configuration["Authentication:Audience"] ?? "AppDeCaixa.Client";
    settings.LifetimeMinutes = 15;  
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Authentication:Issuer"] ?? "AppDeCaixa",
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Authentication:Audience"] ?? "AppDeCaixa.Client",
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKey,
        ValidateLifetime = true,
        RoleClaimType = ClaimTypes.Role,
        NameClaimType = ClaimTypes.Name,
        ClockSkew = TimeSpan.FromSeconds(30)
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("bootstrap", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddScoped<IUserCredentialRepository, UserCredentialRepository>();
builder.Services.AddScoped<PasswordHashService>();
builder.Services.AddScoped<IPasswordVerifier>(services => services.GetRequiredService<PasswordHashService>());
builder.Services.AddScoped<IPasswordHashService>(services => services.GetRequiredService<PasswordHashService>());
builder.Services.AddScoped<SignInService>();
builder.Services.AddScoped<BootstrapManagerService>();
builder.Services.AddScoped<InitialCompanyBootstrapService>();
builder.Services.AddScoped<CreateUserService>();
builder.Services.AddScoped<ICashSessionRepository, CashSessionRepository>();
builder.Services.AddScoped<CashSessionService>();
builder.Services.AddScoped<ICashMovementRepository, CashMovementRepository>();
builder.Services.AddScoped<CashMovementService>();
builder.Services.AddScoped<IServiceOrderRepository, ServiceOrderRepository>();
builder.Services.AddScoped<ServiceOrderService>();
builder.Services.AddScoped<IOrderPaymentRepository, OrderPaymentRepository>();
builder.Services.AddScoped<OrderPaymentService>();
builder.Services.AddScoped<IDebtSettlementRepository, DebtSettlementRepository>();
builder.Services.AddScoped<DebtSettlementService>();
builder.Services.AddScoped<IServiceCatalogRepository, ServiceCatalogRepository>();
builder.Services.AddScoped<ServiceCatalogService>();
builder.Services.AddScoped<ICollaboratorRepository, CollaboratorRepository>();
builder.Services.AddScoped<CollaboratorService>();
builder.Services.AddScoped<ICommissionRepository, CommissionRepository>();
builder.Services.AddScoped<CommissionService>();
builder.Services.AddScoped<IEmployeeCommissionRepository, EmployeeCommissionRepository>();
builder.Services.AddScoped<EmployeeCommissionService>();
builder.Services.AddScoped<IAccessTokenIssuer, JwtAccessTokenIssuer>();

var app = builder.Build();

if (builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(configuredSigningKey))
{
    app.Logger.LogWarning("Using an ephemeral development JWT key; issued tokens expire on API restart.");
}

app.UseExceptionHandler();
app.UseRouting();
if (!app.Environment.IsDevelopment() &&
    app.Configuration.GetValue("Hosting:RedirectToHttps", true))
{
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health").WithTags("Health");
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapCashSessionEndpoints();
app.MapCashMovementEndpoints();
app.MapServiceOrderEndpoints();
app.MapPaymentEndpoints();
app.MapDebtSettlementEndpoints();
app.MapServiceEndpoints();
app.MapCollaboratorEndpoints();
app.MapCommissionEndpoints();
app.MapEmployeeCommissionEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();

public partial class Program;
