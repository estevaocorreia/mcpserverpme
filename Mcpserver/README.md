# 📡 Observabilidade — MCP Server

Stack de observabilidade baseada em **OpenTelemetry**, **Prometheus** e **Grafana**.

---

## Arquitetura

```
MCP Server (C#)
  ├── Traces  ──→ gRPC :4317  ──→ otel-collector ──→ debug log
  ├── Metrics ──→ gRPC :4317  ──→ otel-collector ──→ Prometheus :8889
  └── Logs    ──→ HTTP :4318  ──→ otel-collector ──→ debug log
                                        ↑
                                Prometheus scrape :8889
                                        ↓
                                   Grafana dashboard
```

---

## O que é rastreado

### 🔧 Tools MCP
Cada tool instrumentada gera automaticamente:

| Métrica | Descrição |
|--------|-----------|
| `mcp_tool_calls_total` | Total de chamadas por tool e usuário |
| `mcp_tool_errors_total` | Total de erros por tool, usuário e tipo de erro |
| `mcp_tool_duration_ms` | Histograma de duração em ms por tool |
| `mcp_tool_rows_returned` | Registros retornados por chamada |

**Tags disponíveis em todas as métricas:**
- `tool` — nome da tool (`get_alarm_events`, `meter_energy`, etc.)
- `user` — nome do cliente MCP (`claude-desktop`, `cursor`, etc.)
- `error` — tipo da exceção (apenas em erros)

### 🗄️ Banco de dados
Cada query/SP executada gera um **span filho** no trace da tool com:
- `db.system` = `sqlserver`
- `db.name` = nome do banco (`ION_Data`, `ION_Network`)
- `db.statement` = nome da SP ou query
- `db.duration_ms` = tempo de execução

### 🌐 HTTP (automático)
- `http_server_request_duration_seconds` — latência por rota
- `http_server_active_requests` — requisições simultâneas
- `kestrel_*` — métricas do servidor web

### ⚙️ Runtime .NET (automático)
- `process_runtime_dotnet_gc_*` — Garbage Collector
- `process_runtime_dotnet_thread_pool_*` — Thread Pool
- `process_runtime_dotnet_jit_*` — JIT Compiler

---

## Health Check

**Endpoint:** `GET /health`

```json
{
  "status": "healthy",
  "service": "mcpserver",
  "utc": "2026-03-10T14:00:00Z",
  "machine": "VM-024",
  "uptime_seconds": 3600,
  "memory": {
    "working_set_mb": 92.5,
    "private_memory_mb": 157.0
  },
  "database": {
    "connected": true,
    "latency_ms": 12.3,
    "error": null
  },
  "tools": {
    "total_calls": 1523,
    "total_errors": 2
  }
}
```

Retorna `200` quando saudável e `503` quando o banco está inacessível.

---

## Subindo o stack

```bash
# Na pasta otel/
docker compose -f docker-compose.otel.yml up -d

# Verificar logs do collector
docker logs otel-collector -f

# Verificar health do collector
curl http://localhost:13133/

# Verificar métricas expostas
curl http://localhost:8889/metrics | grep mcp_tool
```

---

## Configuração no Grafana

### 1. Adicionar datasource Prometheus

- Acesse: `https://<seu-host>/grafana/connections/datasources/new`
- Tipo: **Prometheus**
- URL: `http://memt_prometheus:9090`
- Clique **Save & Test**

### 2. Importar dashboard pronto (.NET + OTEL)

- Acesse: `https://<seu-host>/grafana/dashboard/import`
- ID: **`19924`** → clique **Load**
- Selecione o datasource criado → **Import**

### 3. Dashboard customizado — JSON sugerido

Crie um novo dashboard e importe o JSON abaixo via **Dashboard → Import → Cole o JSON**:

```json
{
  "title": "MCP Server — Observabilidade",
  "uid": "mcpserver-otel",
  "schemaVersion": 38,
  "time": { "from": "now-3h", "to": "now" },
  "refresh": "30s",
  "panels": [
    {
      "id": 1,
      "title": "🔢 Total de chamadas por Tool",
      "type": "bargauge",
      "gridPos": { "x": 0, "y": 0, "w": 12, "h": 8 },
      "targets": [
        {
          "expr": "sum by (tool) (mcp_tool_calls_total{job=\"mcpserver\"})",
          "legendFormat": "{{tool}}"
        }
      ],
      "options": { "orientation": "horizontal", "reduceOptions": { "calcs": ["lastNotNull"] } }
    },
    {
      "id": 2,
      "title": "👤 Chamadas por Usuário",
      "type": "bargauge",
      "gridPos": { "x": 12, "y": 0, "w": 12, "h": 8 },
      "targets": [
        {
          "expr": "sum by (user) (mcp_tool_calls_total{job=\"mcpserver\"})",
          "legendFormat": "{{user}}"
        }
      ],
      "options": { "orientation": "horizontal", "reduceOptions": { "calcs": ["lastNotNull"] } }
    },
    {
      "id": 3,
      "title": "⚠️ Taxa de Erros por Tool (%)",
      "type": "bargauge",
      "gridPos": { "x": 0, "y": 8, "w": 12, "h": 8 },
      "targets": [
        {
          "expr": "100 * sum by (tool) (mcp_tool_errors_total{job=\"mcpserver\"}) / sum by (tool) (mcp_tool_calls_total{job=\"mcpserver\"})",
          "legendFormat": "{{tool}}"
        }
      ],
      "fieldConfig": {
        "defaults": {
          "unit": "percent",
          "thresholds": {
            "steps": [
              { "color": "green", "value": 0 },
              { "color": "yellow", "value": 5 },
              { "color": "red", "value": 15 }
            ]
          }
        }
      }
    },
    {
      "id": 4,
      "title": "⏱️ Latência p95 por Tool (ms)",
      "type": "bargauge",
      "gridPos": { "x": 12, "y": 8, "w": 12, "h": 8 },
      "targets": [
        {
          "expr": "histogram_quantile(0.95, sum by (tool, le) (rate(mcp_tool_duration_ms_bucket{job=\"mcpserver\"}[5m])))",
          "legendFormat": "{{tool}}"
        }
      ],
      "fieldConfig": {
        "defaults": {
          "unit": "ms",
          "thresholds": {
            "steps": [
              { "color": "green", "value": 0 },
              { "color": "yellow", "value": 1000 },
              { "color": "red", "value": 5000 }
            ]
          }
        }
      }
    },
    {
      "id": 5,
      "title": "📊 Registros retornados por Tool (média)",
      "type": "timeseries",
      "gridPos": { "x": 0, "y": 16, "w": 24, "h": 8 },
      "targets": [
        {
          "expr": "rate(mcp_tool_rows_returned_sum{job=\"mcpserver\"}[5m]) / rate(mcp_tool_rows_returned_count{job=\"mcpserver\"}[5m])",
          "legendFormat": "{{tool}}"
        }
      ]
    },
    {
      "id": 6,
      "title": "🧠 Memória da aplicação (MB)",
      "type": "timeseries",
      "gridPos": { "x": 0, "y": 24, "w": 12, "h": 8 },
      "targets": [
        {
          "expr": "process_runtime_dotnet_gc_heap_size_bytes{job=\"mcpserver\"} / 1024 / 1024",
          "legendFormat": "GC Heap {{generation}}"
        }
      ],
      "fieldConfig": { "defaults": { "unit": "decmbytes" } }
    },
    {
      "id": 7,
      "title": "🧵 Thread Pool",
      "type": "timeseries",
      "gridPos": { "x": 12, "y": 24, "w": 12, "h": 8 },
      "targets": [
        {
          "expr": "process_runtime_dotnet_thread_pool_threads_count{job=\"mcpserver\"}",
          "legendFormat": "Threads ativos"
        },
        {
          "expr": "process_runtime_dotnet_thread_pool_queue_length{job=\"mcpserver\"}",
          "legendFormat": "Fila de trabalho"
        }
      ]
    }
  ]
}
```

---

## Queries úteis no Prometheus

```promql
# Tools mais chamadas
topk(5, sum by (tool) (mcp_tool_calls_total))

# Usuários mais ativos
topk(5, sum by (user) (mcp_tool_calls_total))

# Taxa de erro geral
sum(mcp_tool_errors_total) / sum(mcp_tool_calls_total) * 100

# Latência p95 da última hora
histogram_quantile(0.95, sum by (tool, le) (rate(mcp_tool_duration_ms_bucket[1h])))

# Chamadas por minuto (rate)
sum by (tool) (rate(mcp_tool_calls_total[1m]))

# Tools com erro nos últimos 5 min
sum by (tool, error) (increase(mcp_tool_errors_total[5m])) > 0
```

---

## Alertas sugeridos no Grafana

| Alerta | Condição | Severidade |
|--------|----------|------------|
| Alta taxa de erros | `mcp_tool_errors_total / mcp_tool_calls_total > 0.05` | Warning |
| Tool muito lenta | `p95 latência > 5000ms por 5min` | Warning |
| Banco inacessível | `/health` retorna `503` | Critical |
| Memória alta | `working_set > 500MB` | Warning |
| Sem chamadas | `rate(mcp_tool_calls_total[30m]) == 0` | Info |