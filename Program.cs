using AutoMapper;
using CorporAIte;
using CorporAIte.Profiles;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);
var appConfig = builder.Configuration.Get<AppConfig>();

// Add services to the container.
builder.Services.AddSingleton<CorporAIteService>();
builder.Services.AddSingleton<AIService>(a => new AIService(appConfig.OpenAI));
builder.Services.AddSingleton<SharePointService>(y => new SharePointService(appConfig.SharePoint.TenantName, appConfig.SharePoint.ClientId, appConfig.SharePoint.ClientSecret));

var mapperConfig = new MapperConfiguration(mc =>
{
    mc.AddProfile(new ChatProfile());
});

IMapper mapper = mapperConfig.CreateMapper();
builder.Services.AddSingleton(mapper);

builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
