# Л/Р 4. Экспорт логов

Приложение собирает логи при помощи **Serilog** → **Loki** (напрямую через GrafanaLoki sink) и визуализирует их в **Grafana**.

### Архитектура логирования

```
PlantManager (C# package Serilog.Sinks.Grafana.Loki)
    ↓
Loki (хранение логов)
    ↓
Grafana (визуализация + запросы)
```

### Labels

Логи отправляются с labels:
- `app=plantmanager`
- `service=plantmanager`
- `level` (info, warning, error)
- `Environment` (Development, Production)

### События

| Событие | Уровень | Описание |
|---------|---------|----------|
| Создание устройства | Information | `Device created: {DeviceId}, Name: {Name}, Type: {Type}` |
| Удаление устройства | Information | `Device deleted: {DeviceId}` |
| Запуск полива | Information | `Watering started: DeviceId={DeviceId}, Duration={Duration}s` |
| Данные с датчика | Debug | `Sensor data received: DeviceId={DeviceId}, Temperature={Temperature}, Humidity={Humidity}` |

### Примеры запросов в Loki (LogQL) через Grafana

Топ 5 устройств по количеству поливов
![1](docs/logs-1.png)

Все логи
![2](docs/logs-2.png)

Поиск логов по конкретному устройтву
![3](docs/logs-3.png)

Поиск логов по конкретному событию
![4](docs/logs-4.png)

---

## Л/Р 5. Экспорт трейсов

Приложение собирает трейсы при помощи **OpenTelemetry** → **Tempo** и визуализирует их в **Grafana**.

### Архитектура трейсинга

```
PlantManager (OpenTelemetry SDK)
    ↓
Tempo (хранение трейсов)
    ↓
Grafana (визуализация + запросы)
```

### Технические решения

- **OpenTelemetry** - стандарт для распределённой трассировки
- **Tempo** - storage для трейсов от Grafana (порт 3200)
- **Экспорт через OTLP** - протокол gRPC (порт 4317)
- **TraceEnricher** - обогащение логов trace_id для связи логов и трейсов

### Эндпоинты для тестирования трейсов

| Эндпоинт | Описание | Количество спанов |
|----------|----------|-------------------|
| `GET /trace/simple` | Простой трейс с одним спаном | 1 |
| `GET /trace/complex` | Сложный трейс с вложенными спанами | 4 |

### Как посмотреть трейсы

1. Запустить `docker-compose up --build`
2. Открыть Grafana: http://localhost:3000 (admin/admin)
3. Перейти в Explore → выбрать Tempo
4. Выполнить запрос к `/trace/simple` или `/trace/complex`
5. Найти трейс в Tempo по service name "plantmanager"

### Как связать логи и трейсы

В ответах эндпоинтов возвращается `trace_id`, который можно использовать для поиска трейса:

```bash
curl http://localhost:5000/trace/complex
# Ответ: {"message":"Сложный трейс выполнен","trace_id":"a1b2c3d4e5f6...","spans":4}
```

Также логи автоматически обогащаются `trace_id` и `span_id` через TraceEnricher.