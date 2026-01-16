using PortListener.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Register in-memory weight storage service
builder.Services.AddSingleton<IWeightStorageService, WeightStorageService>();

// Register background service for TCP listener
builder.Services.AddHostedService<ScaleListenerService>();

// Configure CORS for Next.js frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowNextJs", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseCors("AllowNextJs");
app.UseAuthorization();
app.MapControllers();

// Configure to run on localhost:7001
app.Urls.Add("http://localhost:7001");

app.Run();
