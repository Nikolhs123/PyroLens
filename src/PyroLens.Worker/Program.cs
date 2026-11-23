using PyroLens.Infrastructure;
using PyroLens.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPyroLensIngestion(builder.Configuration);
builder.Services.AddPyroLensForecasting(builder.Configuration);
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHostedService<ForecastWorker>();

builder.Build().Run();
