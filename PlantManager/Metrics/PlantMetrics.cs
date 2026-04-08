using Prometheus;

namespace PlantManager.Metrics;

public class PlantMetrics
{
    private readonly Histogram _wateringDuration = Prometheus.Metrics.CreateHistogram(
        "plantmanager_watering_duration_seconds",
        "Длительность полива в секундах",
        new HistogramConfiguration
        {
            LabelNames = new[] { "device_id" },
            Buckets = new double[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512 }
        });

    private readonly Counter _wateringOperations = Prometheus.Metrics.CreateCounter(
        "plantmanager_watering_operations_total",
        "Общее количество операций полива",
        new CounterConfiguration
        {
            LabelNames = new[] { "device_id" }
        });

    private readonly Gauge _devicesTotal = Prometheus.Metrics.CreateGauge(
        "plantmanager_devices_total",
        "Общее количество устройств");

    private readonly Counter _devicesCreated = Prometheus.Metrics.CreateCounter(
        "plantmanager_devices_created_total",
        "Общее количество созданных устройств");

    private readonly Counter _devicesDeleted = Prometheus.Metrics.CreateCounter(
        "plantmanager_devices_deleted_total",
        "Общее количество удалённых устройств");

    private readonly Counter _sensorDataEntries = Prometheus.Metrics.CreateCounter(
        "plantmanager_sensor_data_entries_total",
        "Общее количество записей данных с датчиков",
        new CounterConfiguration
        {
            LabelNames = new[] { "device_id" }
        });

    private readonly Histogram _requestDuration = Prometheus.Metrics.CreateHistogram(
        "plantmanager_http_request_duration_seconds",
        "Длительность HTTP запросов в секундах",
        new HistogramConfiguration
        {
            LabelNames = new[] { "method", "endpoint" },
            Buckets = new double[] { 0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5 }
        });

    public void RecordWatering(int durationSeconds, int deviceId)
    {
        _wateringDuration.WithLabels(deviceId.ToString()).Observe(durationSeconds);
        _wateringOperations.WithLabels(deviceId.ToString()).Inc();
    }

    public void UpdateDevicesCount(int count)
    {
        _devicesTotal.Set(count);
    }

    public void RecordDeviceCreated()
    {
        _devicesCreated.Inc();
    }

    public void RecordDeviceDeleted()
    {
        _devicesDeleted.Inc();
    }

    public void RecordSensorData(int deviceId)
    {
        _sensorDataEntries.WithLabels(deviceId.ToString()).Inc();
    }

    public void RecordRequest(string method, string endpoint, double durationSeconds)
    {
        _requestDuration.WithLabels(method, endpoint).Observe(durationSeconds);
    }
}