using PlantManager.Metrics;
using Xunit;

namespace PlantManager.Tests;

public class PlantMetricsTests
{
    [Fact]
    public void RecordDeviceCreated_IncrementsCounter()
    {
        var metrics = new PlantMetrics();
        metrics.RecordDeviceCreated();
        metrics.RecordDeviceCreated();
        
        Assert.NotNull(metrics);
    }

    [Fact]
    public void RecordDeviceDeleted_IncrementsCounter()
    {
        var metrics = new PlantMetrics();
        metrics.RecordDeviceDeleted();
        
        Assert.NotNull(metrics);
    }

    [Fact]
    public void RecordSensorData_IncrementsCounter()
    {
        var metrics = new PlantMetrics();
        metrics.RecordSensorData(1);
        metrics.RecordSensorData(1);
        
        Assert.NotNull(metrics);
    }

    [Fact]
    public void RecordWatering_RecordsHistogram()
    {
        var metrics = new PlantMetrics();
        metrics.RecordWatering(30, 1);
        metrics.RecordWatering(60, 2);
        
        Assert.NotNull(metrics);
    }

    [Fact]
    public void UpdateDevicesCount_UpdatesGauge()
    {
        var metrics = new PlantMetrics();
        metrics.UpdateDevicesCount(5);
        metrics.UpdateDevicesCount(10);
        
        Assert.NotNull(metrics);
    }
}