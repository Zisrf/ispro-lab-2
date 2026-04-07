using Prometheus;

namespace PlantManager.Metrics;

public class PlantMetrics
{
    private readonly Histogram _wateringDuration = Prometheus.Metrics.CreateHistogram(
        "plantmanager_watering_duration_seconds",
        "Длительность полива в секундах",
        new HistogramConfiguration
        {
            LabelNames = new[] { "device_type" },
            Buckets = new double[] { 10, 30, 60, 120, 300, 600 }
        });

    private readonly Gauge _devicesTotal = Prometheus.Metrics.CreateGauge(
        "plantmanager_devices_total",
        "Общее количество устройств");

    private readonly Counter _sensorDataReceived = Prometheus.Metrics.CreateCounter(
        "plantmanager_sensor_data_received_total",
        "Общее количество полученных показаний датчиков",
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
            Buckets = new[] { 0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5 }
        });

    public void RecordWatering(int durationSeconds, string deviceType)
    {
        _wateringDuration.WithLabels(deviceType).Observe(durationSeconds);
    }

    public void UpdateDevicesCount(int count)
    {
        _devicesTotal.Set(count);
    }

    public void RecordSensorData(int deviceId)
    {
        _sensorDataReceived.WithLabels(deviceId.ToString()).Inc();
    }

    public void RecordRequest(string method, string endpoint, double durationSeconds)
    {
        _requestDuration.WithLabels(method, endpoint).Observe(durationSeconds);
    }
}