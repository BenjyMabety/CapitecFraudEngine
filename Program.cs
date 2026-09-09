using CapitecFraudEngine.Api;
using CapitecFraudEngine.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Register Swagger Services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "https://localhost:4200")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Register Engine Evaluators
builder.Services.AddSingleton<IFraudRuleEvaluator, NumericRuleEvaluator>();
builder.Services.AddSingleton<IFraudRuleEvaluator, StringRuleEvaluator>();
builder.Services.AddSingleton<FraudEvaluationEngine>();

// Register ETL & Archive Services
builder.Services.AddSingleton<ArchiveService>();
builder.Services.AddHostedService<FileCollectorService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Use CORS
app.UseCors("AllowAngularFrontend");

// Map Route Handlers
app.MapEndpoints();

app.Run();