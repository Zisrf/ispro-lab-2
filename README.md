## Л/Р 4. Экспорт логов

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