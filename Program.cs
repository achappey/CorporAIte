using AutoMapper;
using CorporAIte;
using CorporAIte.Profiles;

// Configure web application
var builder = WebApplication.CreateBuilder(args);
var appConfig = builder.Configuration.Get<AppConfig>();

// Register services
builder.Services.AddSingleton<AppConfig>(appConfig);
builder.Services.AddSingleton<CorporAIteService>();
builder.Services.AddSingleton<AIService>();
builder.Services.AddSingleton<SharePointService>();
builder.Services.AddSingleton<SharePointAIService>();

// Configure in-memory cache
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICacheService, InMemoryCacheService>();

// Configure AutoMapper
var mapperConfig = new MapperConfiguration(mc =>
{
    mc.AddProfile(new ChatProfile());
});
IMapper mapper = mapperConfig.CreateMapper();
builder.Services.AddSingleton(mapper);

// Add controllers
builder.Services.AddControllers();

// Configure Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Build the application
var app = builder.Build();

// Use Swagger and Swagger UI in non-production environments
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Configure middleware
app.UseHttpsRedirection();
app.UseAuthorization();

// Map controllers
app.MapControllers();

// Run the application
app.Run();
