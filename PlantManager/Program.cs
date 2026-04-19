using System.Diagnostics;
using Microsoft.OpenApi.Models;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PlantManager;
using PlantManager.Metrics;
using Prometheus;
using Serilog;
using Serilog.Sinks.Grafana.Loki;

const string serviceName = "plantmanager";
var builder = WebApplication.CreateBuilder(args);

var lokiUrl = builder.Configuration["Loki:Url"] ?? throw new InvalidOperationException("Loki:Url is not set");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.WithEnvironmentName()
    .Enrich.FromLogContext()
    .Enrich.With<TraceEnricher>()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .WriteTo.GrafanaLoki(
        uri: lokiUrl,
        labels: new List<LokiLabel>
        {
            new() { Key = "app", Value = "plantmanager" },
            new() { Key = "service", Value = serviceName }
        },
        propertiesAsLabels: ["level", "service", "Environment"],
        credentials: null
    )
    .CreateLogger();

builder.Host.UseSerilog();

var tempoUrl = builder.Configuration["Tempo:Url"] ?? throw new InvalidOperationException("Tempo:Url is not set");
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(serviceName)
        .ConfigureResource(resource => resource.AddService(serviceName: serviceName, serviceVersion: "lab5"))
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options => { options.Endpoint = new Uri(tempoUrl); }));

builder.Services.AddSingleton(new ActivitySource(serviceName));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Simple Plant Manager API",
        Version = "1.0",
        Description = "Простое API для управления датчиками и поливом растений"
    });
});

var plantMetrics = new PlantMetrics();
builder.Services.AddSingleton(plantMetrics);

var app = builder.Build();

var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".yaml"] = "application/yaml";
contentTypeProvider.Mappings[".yml"]  = "application/yaml";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider
});

app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi.yaml", "Plant Manager API v1");
    options.RoutePrefix = "swagger";
});

app.UseHttpMetrics();
app.MapMetrics();

var devices = new System.Collections.Concurrent.ConcurrentDictionary<int, Device>();
var sensorDataStore = new System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Concurrent.ConcurrentBag<SensorData>>();
var deviceTypes = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
var nextId = 0;

app.MapGet("/devices", () => Results.Ok(devices.Values))
   .WithName("GetDevices")
   .WithTags("Devices")
   .Produces<List<Device>>(200);

app.MapPost("/devices", (DeviceInput input) =>
{
    var id = Interlocked.Increment(ref nextId);
    var device = new Device(id, input.Name, input.Type);
    devices[id] = device;
    sensorDataStore[id] = new System.Collections.Concurrent.ConcurrentBag<SensorData>();
    deviceTypes[id] = input.Type;
    plantMetrics.RecordDeviceCreated();
    plantMetrics.UpdateDevicesCount(devices.Count);
    Log.Information("Device created: {DeviceId}, Name: {Name}, Type: {Type}", id, input.Name, input.Type);
    return Results.Created($"/devices/{device.Id}", device);
})
   .WithName("CreateDevice")
   .WithTags("Devices")
   .Produces<Device>(201)
   .ProducesValidationProblem();

app.MapGet("/devices/{id:int}", (int id) =>
{
    return devices.TryGetValue(id, out var device) ? Results.Ok(device) : Results.NotFound();
})
   .WithName("GetDevice")
   .WithTags("Devices")
   .Produces<Device>(200)
   .Produces(404);

app.MapDelete("/devices/{id:int}", (int id) =>
{
    if (!devices.TryRemove(id, out _)) return Results.NotFound();
    sensorDataStore.TryRemove(id, out _);
    deviceTypes.TryRemove(id, out _);
    plantMetrics.RecordDeviceDeleted();
    plantMetrics.UpdateDevicesCount(devices.Count);
    Log.Information("Device deleted: {DeviceId}", id);
    return Results.NoContent();
})
   .WithName("DeleteDevice")
   .WithTags("Devices")
   .Produces(204)
   .Produces(404);

app.MapPost("/devices/{id:int}/data", (int id, SensorData data) =>
{
    if (!sensorDataStore.TryGetValue(id, out var bag)) return Results.NotFound();
    var entry = data with { Timestamp = data.Timestamp ?? DateTime.UtcNow };
    bag.Add(entry);
    plantMetrics.RecordSensorData(id);
    Log.Debug("Sensor data received: DeviceId={DeviceId}, Temperature={Temperature}, Humidity={Humidity}", 
        id, entry.Temperature, entry.Humidity);
    return Results.Ok(entry);
})
   .WithName("PostSensorData")
   .WithTags("SensorData")
   .Produces<SensorData>(200)
   .Produces(404);

app.MapGet("/devices/{id:int}/data", (int id) =>
{
    if (!sensorDataStore.TryGetValue(id, out var bag)) return Results.NotFound();
    return Results.Ok(bag.ToList());
})
   .WithName("GetSensorData")
   .WithTags("SensorData")
   .Produces<List<SensorData>>(200)
   .Produces(404);

app.MapPost("/water", (WaterCommand cmd) =>
{
    if (!devices.ContainsKey(cmd.DeviceId)) return Results.NotFound();
    plantMetrics.RecordWatering(cmd.Duration, cmd.DeviceId);
    Log.Information("Watering started: DeviceId={DeviceId}, Duration={Duration}s", cmd.DeviceId, cmd.Duration);
    return Results.Ok(new { message = $"Полив запущен для устройства {cmd.DeviceId} на {cmd.Duration} секунд." });
})
   .WithName("StartWatering")
   .WithTags("Watering")
   .Produces(200)
   .Produces(404);

app.MapGet("/trace/simple", (ActivitySource activitySource) =>
{
    using var activity = activitySource.StartActivity("SimpleTrace");
    activity?.SetTag("trace.type", "simple");
    activity?.SetTag("description", "Простой трейс с одним спаном");
    return Results.Ok(new { message = "Простой трейс выполнен", trace_id = activity?.Id });
})
   .WithName("SimpleTrace")
   .WithTags("Traces")
   .Produces(200);

app.MapGet("/trace/complex", (ActivitySource activitySource) =>
{
    using var rootActivity = activitySource.StartActivity("ComplexTrace");
    rootActivity?.SetTag("trace.type", "complex");
    rootActivity?.SetTag("description", "Сложный трейс с несколькими вложенными спанами");
    
    using var validationActivity = activitySource.StartActivity("ValidateRequest");
    validationActivity?.SetTag("step", "validation");
    Thread.Sleep(50);
    validationActivity?.SetTag("validation.result", "success");
    
    using var processingActivity = activitySource.StartActivity("ProcessData");
    processingActivity?.SetTag("step", "processing");
    
    using var dbActivity = activitySource.StartActivity("SaveToDatabase");
    dbActivity?.SetTag("step", "database");
    Thread.Sleep(100);
    dbActivity?.SetTag("db.operation", "insert");
    
    using var notifyActivity = activitySource.StartActivity("SendNotification");
    notifyActivity?.SetTag("step", "notification");
    Thread.Sleep(30);
    notifyActivity?.SetTag("notification.type", "email");
    
    processingActivity?.SetTag("processing.result", "completed");
    
    return Results.Ok(new { 
        message = "Сложный трейс выполнен", 
        trace_id = rootActivity?.Id,
        spans = 4
    });
})
   .WithName("ComplexTrace")
   .WithTags("Traces")
   .Produces(200);

app.Run();

record Device(int Id, string Name, string Type);

record DeviceInput(string Name, string Type);

record SensorData(
    double? Temperature,
    double? Humidity,
    double? SoilMoisture,
    DateTime? Timestamp);

record WaterCommand(int DeviceId, int Duration);