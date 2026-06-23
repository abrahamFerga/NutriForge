using NutriForge.ImportWorker;

var builder = Host.CreateApplicationBuilder(args);

// OpenTelemetry + health checks + resilience, same as the API.
builder.AddServiceDefaults();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
