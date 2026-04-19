using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace PlantManager;

[ExcludeFromCodeCoverage(Justification = "Инфраструктурная настройка")]
public class TraceEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity != null)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("trace_id", activity.TraceId.ToString()));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("span_id", activity.SpanId.ToString()));
        }
    }
}