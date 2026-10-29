using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MonthExpenses.Api.Data;
using MonthExpenses.Api.Interfaces;
using MonthExpenses.Api.Repositories;
using MonthExpenses.Api.Services;
using Microsoft.OpenApi.Models;
using Polly;

EnsureNeonProxyRunning();

var builder = WebApplication.CreateBuilder(args);

// 1. Add PostgreSQL DbContext with Neon Serverless Retry Strategy
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
builder.Services.AddDbContext<ExpenseDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        // Resiliency for Neon PostgreSQL serverless compute cold starts and sleep-wake cycles
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
        npgsqlOptions.CommandTimeout(30);
    }));

// 2. Register Repositories and Services
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IBudgetRepository, BudgetRepository>();
builder.Services.AddScoped<IAuthRepository, AuthRepository>();

// Neon Serverless Database Keep-Alive Background Service
builder.Services.AddHostedService<NeonKeepAliveService>();

// 3. Configure JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "KharchaSecureJwtTokenKey2026_WithHighEntropyAtLeast256BitsLong!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "KharchaApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "KharchaClient";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(authHeader))
            {
                var token = authHeader.Trim();
                // Strip "Bearer " prefix if provided, or accept raw token directly
                while (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = token.Substring(7).Trim();
                }
                context.Token = token;
            }
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();

// Add CORS to allow frontend clients to connect from anywhere
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 4. Configure Rate Limiter (Auth & General API endpoints)
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rateLimiterOptions.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        var responseObj = new
        {
            statusCode = 429,
            message = "Too many requests. Rate limit active. Please wait a moment before trying again.",
            retryAfterSeconds = 60
        };
        await context.HttpContext.Response.WriteAsJsonAsync(responseObj, token);
    };

    // Strict policy for Auth endpoints (Login & Register): 5 attempts per minute per IP
    rateLimiterOptions.AddPolicy("auth-policy", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    // General API policy: 100 requests per minute per IP
    rateLimiterOptions.AddPolicy("api-policy", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 4,
                QueueLimit = 0
            });
    });
});

// 6. Add Controllers and OpenAPI / Swagger with JWT Bearer Authorize
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MonthExpenses API",
        Version = "v1",
        Description = "Expense Tracker Web API with JWT Bearer Authentication"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Enter JWT token in format: Bearer {token} (or simply paste your token directly)",
        In = ParameterLocation.Header,
        
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// 7. Ensure database schema & seed sample data
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ExpenseDbContext>();
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""Subcategories"" (
            ""Id"" SERIAL PRIMARY KEY,
            ""Name"" VARCHAR(50) NOT NULL,
            ""CategoryId"" INTEGER NOT NULL REFERENCES ""Categories""(""Id"") ON DELETE CASCADE,
            ""IsDefault"" BOOLEAN NOT NULL DEFAULT FALSE,
            ""CreatedBy"" INTEGER NOT NULL DEFAULT 1,
            ""CreatedDate"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
            ""UpdatedBy"" INTEGER NULL,
            ""UpdatedDate"" TIMESTAMP WITH TIME ZONE NULL,
            ""IsDeleted"" INTEGER NOT NULL DEFAULT 0
        );
        ALTER TABLE ""Categories"" ADD COLUMN IF NOT EXISTS ""UserId"" INTEGER;
        ALTER TABLE ""Subcategories"" ADD COLUMN IF NOT EXISTS ""UserId"" INTEGER;
        ALTER TABLE ""Transactions"" ADD COLUMN IF NOT EXISTS ""SubcategoryId"" INTEGER NULL REFERENCES ""Subcategories""(""Id"") ON DELETE SET NULL;
        ALTER TABLE ""Transactions"" ADD COLUMN IF NOT EXISTS ""Subcategory"" VARCHAR(50) NULL;
        CREATE INDEX IF NOT EXISTS ""IX_Subcategories_CategoryId_Name_CreatedBy"" ON ""Subcategories"" (""CategoryId"", ""Name"", ""CreatedBy"");
        CREATE INDEX IF NOT EXISTS ""IX_Transactions_SubcategoryId"" ON ""Transactions"" (""SubcategoryId"");
    ");

    await db.SeedSampleDataAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning("Schema migration warning: {Message}", ex.Message);
}


// 8. Configure HTTP request pipeline
app.UseCors("AllowAll");

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MonthExpenses API v1");
    c.RoutePrefix = "swagger";
});

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// 9. Lightweight Health Check & Keep-Alive Endpoint
// Accessible without authentication so uptime pingers (cron-job.org / UptimeRobot) can keep Render & Neon awake 24/7
app.MapGet("/api/health", async (ExpenseDbContext db) =>
{
    try
    {
        // Executes a lightweight query (0ms execution) to keep Neon Postgres connection & compute awake
        await db.Database.ExecuteSqlRawAsync("SELECT 1;");
        return Results.Ok(new
        {
            status = "healthy",
            database = "connected",
            service = "MonthExpenses.Api",
            timestamp = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            status = "degraded",
            database = "error",
            error = ex.Message,
            timestamp = DateTime.UtcNow
        }, statusCode: 500);
    }
}).AllowAnonymous();

app.MapControllers();

app.Run();

static void EnsureNeonProxyRunning()
{
    try
    {
        using var client = new System.Net.Sockets.TcpClient();
        var result = client.BeginConnect("127.0.0.1", 5433, null, null);
        var success = result.AsyncWaitHandle.WaitOne(400);
        if (success && client.Connected)
        {
            client.EndConnect(result);
            return;
        }
    }
    catch { }

    try
    {
        var proxyFile = Path.Combine(AppContext.BaseDirectory, "neon_proxy.js");
        if (!File.Exists(proxyFile))
        {
            proxyFile = Path.Combine(Directory.GetCurrentDirectory(), "neon_proxy.js");
        }
        if (File.Exists(proxyFile))
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"\"{proxyFile}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            System.Diagnostics.Process.Start(psi);
            Thread.Sleep(500);
        }
    }
    catch { }
}
