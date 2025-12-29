using SecureChatServer.Hubs;

var builder = WebApplication.CreateBuilder(args);

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
        Description = "End-to-End Encrypted Chat Server - Relay Only",
        SecurityNote = "This server NEVER sees plaintext messages. All encryption/decryption happens client-side.",
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
Console.WriteLine("Security: Server relay only - NO message decryption");
Console.WriteLine("===========================================");

app.Run();
