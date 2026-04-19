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

app.MapGet("/devices", (ActivitySource activitySource) =>
{
    using var rootActivity = activitySource.StartActivity("GetAllDevices");
    
    List<Device> result;
    {
        using var fetchActivity = activitySource.StartActivity("FetchDevices");
        result = devices.Values.ToList();
        fetchActivity?.SetTag("devices_count", result.Count.ToString());
    }
    
    return Results.Ok(result);
})
   .WithName("GetDevices")
   .WithTags("Devices")
   .Produces<List<Device>>(200);

app.MapPost("/devices", (DeviceInput input, ActivitySource activitySource) =>
{
    using var rootActivity = activitySource.StartActivity("CreateDevice");
    rootActivity?.SetTag("device_name", input.Name);
    rootActivity?.SetTag("device_type", input.Type);
    
    {
        using var validationActivity = activitySource.StartActivity("ValidateInput");
        validationActivity?.SetTag("input_valid", "true");
    }
    
    var id = Interlocked.Increment(ref nextId);
    var device = new Device(id, input.Name, input.Type);
    
    {
        using var createActivity = activitySource.StartActivity("CreateDeviceRecord");
        devices[id] = device;
        sensorDataStore[id] = new System.Collections.Concurrent.ConcurrentBag<SensorData>();
        deviceTypes[id] = input.Type;
        createActivity?.SetTag("device_id", id.ToString());
    }
    
    {
        using var metricsActivity = activitySource.StartActivity("UpdateMetrics");
        plantMetrics.RecordDeviceCreated();
        plantMetrics.UpdateDevicesCount(devices.Count);
    }
    
    {
        using var logActivity = activitySource.StartActivity("WriteLog");
        Log.Information("Device created: {DeviceId}, Name: {Name}, Type: {Type}", id, input.Name, input.Type);
        logActivity?.SetTag("log_level", "information");
    }
    
    var response = new { device, trace_id = rootActivity?.Id };
    return Results.Created($"/devices/{device.Id}", response);
})
   .WithName("CreateDevice")
   .WithTags("Devices")
   .Produces<Device>(201)
   .ProducesValidationProblem();

app.MapGet("/devices/{id:int}", (int id, ActivitySource activitySource) =>
{
    using var rootActivity = activitySource.StartActivity("GetDevice");
    rootActivity?.SetTag("device_id", id.ToString());
    
    Device? device;
    {
        using var fetchActivity = activitySource.StartActivity("FetchDevice");
        var exists = devices.TryGetValue(id, out device);
        fetchActivity?.SetTag("found", exists.ToString());
    }
    
    return device != null ? Results.Ok(device) : Results.NotFound();
})
   .WithName("GetDevice")
   .WithTags("Devices")
   .Produces<Device>(200)
   .Produces(404);

app.MapDelete("/devices/{id:int}", (int id, ActivitySource activitySource) =>
{
    using var rootActivity = activitySource.StartActivity("DeleteDevice");
    rootActivity?.SetTag("device_id", id.ToString());
    
    {
        using var fetchActivity = activitySource.StartActivity("FetchDevice");
        if (!devices.TryRemove(id, out _)) return Results.NotFound();
        fetchActivity?.SetTag("found", "true");
    }
    
    {
        using var cleanupActivity = activitySource.StartActivity("CleanupData");
        sensorDataStore.TryRemove(id, out _);
        deviceTypes.TryRemove(id, out _);
        cleanupActivity?.SetTag("cleanup_complete", "true");
    }
    
    {
        using var metricsActivity = activitySource.StartActivity("UpdateMetrics");
        plantMetrics.RecordDeviceDeleted();
        plantMetrics.UpdateDevicesCount(devices.Count);
    }
    
    {
        using var logActivity = activitySource.StartActivity("WriteLog");
        Log.Information("Device deleted: {DeviceId}", id);
        logActivity?.SetTag("log_level", "information");
    }
    
    return Results.NoContent();
})
   .WithName("DeleteDevice")
   .WithTags("Devices")
   .Produces(204)
   .Produces(404);

app.MapPost("/devices/{id:int}/data", (int id, SensorData data, ActivitySource activitySource) =>
{
    using var rootActivity = activitySource.StartActivity("PostSensorData");
    rootActivity?.SetTag("device_id", id.ToString());
    
    System.Collections.Concurrent.ConcurrentBag<SensorData>? bag;
    {
        using var validationActivity = activitySource.StartActivity("ValidateData");
        if (!sensorDataStore.TryGetValue(id, out bag)) return Results.NotFound();
        validationActivity?.SetTag("data_valid", "true");
    }
    
    SensorData entry;
    {
        using var processActivity = activitySource.StartActivity("ProcessData");
        entry = data with { Timestamp = data.Timestamp ?? DateTime.UtcNow };
        bag.Add(entry);
        processActivity?.SetTag("temperature", entry.Temperature?.ToString() ?? "null");
        processActivity?.SetTag("humidity", entry.Humidity?.ToString() ?? "null");
    }
    
    {
        using var metricsActivity = activitySource.StartActivity("UpdateMetrics");
        plantMetrics.RecordSensorData(id);
    }
    
    {
        using var logActivity = activitySource.StartActivity("WriteLog");
        Log.Debug("Sensor data received: DeviceId={DeviceId}, Temperature={Temperature}, Humidity={Humidity}", 
            id, entry.Temperature, entry.Humidity);
        logActivity?.SetTag("log_level", "debug");
    }
    
    return Results.Ok(entry);
})
   .WithName("PostSensorData")
   .WithTags("SensorData")
   .Produces<SensorData>(200)
   .Produces(404);

app.MapGet("/devices/{id:int}/data", (int id, ActivitySource activitySource) =>
{
    using var rootActivity = activitySource.StartActivity("GetSensorData");
    rootActivity?.SetTag("device_id", id.ToString());
    
    List<SensorData> result;
    {
        using var fetchActivity = activitySource.StartActivity("FetchData");
        if (!sensorDataStore.TryGetValue(id, out var bag)) return Results.NotFound();
        result = bag.ToList();
        fetchActivity?.SetTag("data_count", result.Count.ToString());
    }
    
    return Results.Ok(result);
})
   .WithName("GetSensorData")
   .WithTags("SensorData")
   .Produces<List<SensorData>>(200)
   .Produces(404);

app.MapPost("/water", (WaterCommand cmd, ActivitySource activitySource) =>
{
    using var rootActivity = activitySource.StartActivity("WateringProcess");
    rootActivity?.SetTag("device_id", cmd.DeviceId.ToString());
    rootActivity?.SetTag("duration", cmd.Duration.ToString());
    
    if (!devices.ContainsKey(cmd.DeviceId)) return Results.NotFound();
    
    string? deviceType;
    {
        using var validationActivity = activitySource.StartActivity("ValidateDevice");
        validationActivity?.SetTag("step", "validation");
        deviceType = deviceTypes.GetValueOrDefault(cmd.DeviceId);
        validationActivity?.SetTag("device_type", deviceType ?? "unknown");
    }
    
    {
        using var metricsActivity = activitySource.StartActivity("RecordMetrics");
        plantMetrics.RecordWatering(cmd.Duration, cmd.DeviceId);
        metricsActivity?.SetTag("metrics.recorded", "true");
    }
    
    {
        using var logActivity = activitySource.StartActivity("WriteLog");
        Log.Information("Watering started: DeviceId={DeviceId}, Duration={Duration}s", cmd.DeviceId, cmd.Duration);
        logActivity?.SetTag("log.written", "true");
    }
    
    rootActivity?.SetTag("watering.status", "completed");
    return Results.Ok(new { message = $"Полив запущен для устройства {cmd.DeviceId} на {cmd.Duration} секунд.", trace_id = rootActivity?.Id });
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