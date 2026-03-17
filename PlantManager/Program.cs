using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

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

var devices = new System.Collections.Concurrent.ConcurrentDictionary<int, Device>();
var sensorDataStore = new System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Concurrent.ConcurrentBag<SensorData>>();
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
    return Results.Ok(new { message = $"Полив запущен для устройства {cmd.DeviceId} на {cmd.Duration} секунд." });
})
   .WithName("StartWatering")
   .WithTags("Watering")
   .Produces(200)
   .Produces(404);

app.Run();

record Device(int Id, string Name, string Type);

record DeviceInput(string Name, string Type);

record SensorData(
    double? Temperature,
    double? Humidity,
    double? SoilMoisture,
    DateTime? Timestamp);

record WaterCommand(int DeviceId, int Duration);

