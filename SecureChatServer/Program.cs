using Microsoft.EntityFrameworkCore;
using SecureChatServer.Data;
using SecureChatServer.Data.Repositories;
using SecureChatServer.Hubs;
using SecureChatServer.Security;

var builder = WebApplication.CreateBuilder(args);

// Add Entity Framework Core with SQL Server
builder.Services.AddDbContext<SecureChatDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();

// Register security services
builder.Services.AddSingleton<RateLimiterService>();
builder.Services.AddSingleton<AttackDetectionService>();

// Add SignalR services
builder.Services.AddSignalR(options =>
{
    // Configure SignalR options for security and performance
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 65536; // 64KB max message size
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

// Configure CORS for the WPF client
builder.Services.AddCors(options =>
{
    options.AddPolicy("SecureChatPolicy", policy =>
    {
        // In production, restrict to specific origins
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Ensure database is created
try
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<SecureChatDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    logger.LogInformation("Attempting to create database...");
    
    // This will create the database and all tables if they don't exist
    bool created = await dbContext.Database.EnsureCreatedAsync();
    
    if (created)
    {
        logger.LogInformation("Database created successfully!");
    }
    else
    {
        logger.LogInformation("Database already exists.");
    }
    
    // Test the connection
    bool canConnect = await dbContext.Database.CanConnectAsync();
    logger.LogInformation("Database connection test: {Result}", canConnect ? "SUCCESS" : "FAILED");
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR: Failed to initialize database: {ex.Message}");
    Console.WriteLine($"Connection String: {builder.Configuration.GetConnectionString("DefaultConnection")}");
    Console.WriteLine("Make sure SQL Server LocalDB is installed or update the connection string.");
    throw;
}

// Use CORS
app.UseCors("SecureChatPolicy");

// Use HTTPS redirection in production
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Map the SignalR hub
app.MapHub<ChatHub>("/chathub");

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

// Security endpoints
app.MapGet("/security/logs", (AttackDetectionService detector) => Results.Ok(detector.GetLogs()));
app.MapGet("/security/alerts", (AttackDetectionService detector) => Results.Ok(detector.GetAlerts()));
app.MapGet("/security/dashboard", async (AttackDetectionService detector, IUserRepository users) =>
{
    var onlineUsers = await users.GetOnlineUsernamesAsync();
    var alerts = detector.GetAlerts(20);
    return Results.Ok(new
    {
        ActiveUsers = onlineUsers,
        AlertCount = alerts.Count,
        RecentAlerts = alerts
    });
});
app.MapPost("/security/simulate/{attackType}", (string attackType, AttackDetectionService detector) =>
{
    const string demoUser = "attacker-demo";
    const string demoIp = "127.0.0.250";

    switch (attackType.ToLowerInvariant())
    {
        case "bruteforce":
            detector.SimulateBruteForce(demoUser, demoIp);
            break;
        case "flood":
            detector.SimulateMessageFlood(demoUser, demoIp);
            break;
        case "fakekey":
            detector.SimulateFakeKeyExchange(demoUser, demoIp);
            break;
        default:
            return Results.BadRequest(new { Error = "Unknown simulation type. Use bruteforce, flood, or fakekey." });
    }

    return Results.Ok(new { Message = $"Simulation '{attackType}' executed." });
});

// Info endpoint (development only)
if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Ok(new
    {
        Service = "SecureChatServer",
        Version = "2.0.0",
        Description = "End-to-End Encrypted Chat Server with security analytics",
        Endpoints = new
        {
            SignalRHub = "/chathub",
            Health = "/health",
            SecurityLogs = "/security/logs",
            SecurityAlerts = "/security/alerts",
            SecurityDashboard = "/security/dashboard",
            SecuritySimulation = "/security/simulate/{bruteforce|flood|fakekey}"
        }
    }));
}

Console.WriteLine("===========================================");
Console.WriteLine("  Secure Chat Server - E2E Encrypted");
Console.WriteLine("===========================================");
Console.WriteLine("SignalR Hub: /chathub");
Console.WriteLine("Database: SQL Server");
Console.WriteLine("Security: Server relay only - NO message decryption");
Console.WriteLine("===========================================");

app.Run();
