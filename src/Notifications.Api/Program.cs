using System.Reflection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Notifications.Api.Security;
using Notifications.Application.Interfaces;
using Notifications.Infrastructure.Firebase;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Services;
using Notifications.Infrastructure.Workers;

var builder = WebApplication.CreateBuilder(args);

// Configuration parameters
var connectionString = builder.Configuration.GetConnectionString("NotificationsDatabase")
    ?? "Host=localhost;Port=5435;Database=visionx_notifications;Username=postgres;Password=postgres";

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "visionx-auth-service";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "visionx-app";

// Database Context
builder.Services.AddDbContext<NotificationsDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.MigrationsAssembly("Notifications.Infrastructure");
    });
});

// HTTP Clients
builder.Services.AddHttpClient("JwksClient");
builder.Services.AddHttpClient<IAudienceResolverClient, AudienceResolverClient>();

// Security
builder.Services.AddSingleton<JwksKeyProvider>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
});

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwksKeyProvider>((options, jwksProvider) =>
    {
        var validIssuers = new[] { "visionx-auth-service", "https://auth.visionx.io", jwtIssuer }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var validAudiences = new[] { "visionx-app", "VisionX_Ecosystem", jwtAudience }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = validIssuers,
            ValidateAudience = true,
            ValidAudiences = validAudiences,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            IssuerSigningKeyResolver = (_, _, kid, _) => jwksProvider.ResolveSigningKeys(kid),
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddMemoryCache();

// CORS policy
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Domain & Infrastructure Services
builder.Services.AddSingleton<IPushNotificationProvider, FirebasePushNotificationProvider>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPreferenceService, PreferenceService>();
builder.Services.AddScoped<ICampaignService, CampaignService>();
builder.Services.AddScoped<IAcademicEventProcessor, AcademicEventProcessor>();
builder.Services.AddScoped<ISessionStateService, SessionStateService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

// Background Workers
builder.Services.AddHostedService<NotificationDeliveryWorker>();
builder.Services.AddHostedService<CampaignSchedulerWorker>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "VisionX Notifications Platform API",
        Version = "v1",
        Description = "Standalone microservice for push notifications, device tokens, user inboxes, and campaign management."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the RS256 access JWT obtained from Auth Service."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });

    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// Automatically apply pending EF Core migrations and seed categories
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
    try
    {
        if (db.Database.IsRelational())
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }
        await NotificationsDbContext.SeedInitialCategoriesAsync(db);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Could not run automatic migration on startup. Attempting EnsureCreated fallback.");
        try
        {
            await db.Database.EnsureCreatedAsync();
            await NotificationsDbContext.SeedInitialCategoriesAsync(db);
        }
        catch (Exception innerEx)
        {
            logger.LogError(innerEx, "Both MigrateAsync and EnsureCreatedAsync failed.");
        }
    }
}

app.UseCors();

if (app.Environment.IsDevelopment() || true)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "VisionX Notifications API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
