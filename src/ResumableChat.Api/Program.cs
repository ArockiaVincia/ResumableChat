using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using ResumableChat.Api.Data;
using ResumableChat.Api.Repositories;
using ResumableChat.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();

// Database configuration (SQLite)
var connectionString = builder.Configuration.GetConnectionString("ChatDb") ?? "Data Source=resumablechat.db";
builder.Services.AddDbContext<ChatDbContext>(options =>
    options.UseSqlite(connectionString));

// Dependency Injection
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddSingleton<IFakeResponseGenerator, FakeResponseGenerator>();
builder.Services.AddSingleton<IRunBroadcaster, RunBroadcaster>();

// CORS for local client testing
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Auto-migrate & handle any in-progress runs left over from a previous process restart
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    db.Database.EnsureCreated();

    var repo = scope.ServiceProvider.GetRequiredService<IChatRepository>();
    repo.HandleInterruptedRunsOnStartupAsync().GetAwaiter().GetResult();
}

app.UseCors();

// Serve the functional React client from src/ResumableChat.Client if present
var candidatePaths = new[]
{
    Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "ResumableChat.Client")),
    Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "src", "ResumableChat.Client")),
    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src", "ResumableChat.Client")),
    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "ResumableChat.Client")),
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "ResumableChat.Client")),
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ResumableChat.Client"))
};

var clientDir = candidatePaths.FirstOrDefault(p => Directory.Exists(p) && File.Exists(Path.Combine(p, "index.html")));
if (!string.IsNullOrEmpty(clientDir) && Directory.Exists(clientDir))
{
    var fileProvider = new PhysicalFileProvider(clientDir);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = fileProvider
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = fileProvider
    });

    var indexPath = Path.Combine(clientDir, "index.html");
    app.MapGet("/", () => Results.File(indexPath, "text/html"));
}

app.MapControllers();

app.Run();
