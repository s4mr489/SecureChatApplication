using Microsoft.EntityFrameworkCore;
using SecureChatServer.Data;
using SecureChatServer.Data.Repositories;
using SecureChatServer.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add Entity Framework Core with SQL Server
builder.Services.AddDbContext<SecureChatDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();

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

// Info endpoint (development only)
if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Ok(new
    {
        Service = "SecureChatServer",
        Version = "1.0.0",
        Description = "End-to-End Encrypted Chat Server with SQL Database",
        SecurityNote = "This server NEVER sees plaintext messages. All encryption/decryption happens client-side.",
        Database = "SQL Server (LocalDB)",
        Endpoints = new
        {
            SignalRHub = "/chathub",
            Health = "/health"
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
