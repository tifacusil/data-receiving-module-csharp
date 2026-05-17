# Data Receiving Module (C#)

Parser + publisher microservice for the landslide monitoring system described in Bai et al. (2020). Receives binary DTU frames over TCP, validates them, produces JSON envelopes, publishes them to RabbitMQ with Redis as a durability buffer, and registers itself in Consul for service discovery.

Implements **Card 2A1** (parser + TCP server), **Card 2A2** (Redis buffer + RabbitMQ publishing), and **Card 2A3** (Consul service registration + health check).

---

## Table of contents

1. [Local development](#local-development)
2. [Configuration](#configuration)
3. [Wave 1 infrastructure](#wave-1-infrastructure)
4. [RabbitMQ topology](#rabbitmq-topology)
5. [Redis buffer](#redis-buffer)
6. [Consul service registration](#consul-service-registration)
7. [Deploying to Linux (Ubuntu 24.04)](#deploying-to-linux-ubuntu-2404)
8. [Managing the service](#managing-the-service)
9. [Running multiple instances](#running-multiple-instances)
10. [Running with the Python sender](#running-with-the-python-sender)
11. [Architecture](#architecture)
12. [Sensor types](#sensor-types)
13. [Dead letter format](#dead-letter-format)

---

## Local development

**Requirements:** .NET 8 SDK (`dotnet --version` should show `8.x.x`).

```bash
# Build
dotnet build DataReceiving.sln

# Test — 76 tests run locally with no external services; ~102 run in CI
dotnet test DataReceiving.sln

# Run locally in stdout mode (no external services needed)
dotnet run --project src/DataReceiving

# Run locally against Wave 1 infra — load credentials from .env (gitignored)
source .env && dotnet run --project src/DataReceiving
```

The `.env` file is gitignored and holds real Wave 1 credentials. Copy `deploy/.env.example` as a starting point and fill in the secrets. The `source .env` command exports all `RECEIVER__*` env vars into the current shell before `dotnet run` picks them up.

---

## Configuration

All settings live in `appsettings.json` and can be overridden by environment variables. The convention is `RECEIVER__` prefix with double underscores as section separators. Environment variables always win over the file.

### Core

| Env var | Default | Description |
|---|---|---|
| `RECEIVER__PORT` | `5000` | TCP port the server listens on |
| `RECEIVER__DLXPATH` | `./dlx` | Directory where rejected frames are written |
| `RECEIVER__OUTPUTMODE` | `stdout` | `stdout` · `file` · `null` · `rabbitmq` |
| `RECEIVER__OUTPUTFILEPATH` | `./output` | Directory for JSON files when `OutputMode=file` |

Setting `OutputMode=rabbitmq` activates the Redis buffer and RabbitMQ publisher. The other modes (`stdout`, `file`, `null`) work without any external services and are useful for local development and testing.

### Redis (required when `OutputMode=rabbitmq`)

| Env var | Wave 1 value | Description |
|---|---|---|
| `RECEIVER__REDIS__CONNECTIONSTRING` | `204.168.203.32:6379,password=<secret>` | StackExchange.Redis connection string — include `,password=` inline |
| `RECEIVER__REDIS__BUFFERTLTHOURS` | `24` | How long a message stays buffered if RabbitMQ is down |
| `RECEIVER__REDIS__RETRYINTERVALSECONDS` | `30` | How often `RetryWorker` scans for unsent messages |
| `RECEIVER__REDIS__RETRYMINAGEMINUTES` | `2` | Minimum message age before `RetryWorker` retries it |

### RabbitMQ (required when `OutputMode=rabbitmq`)

| Env var | Wave 1 value | Description |
|---|---|---|
| `RECEIVER__RABBITMQ__HOST` | `204.168.203.32` | Broker hostname or IP |
| `RECEIVER__RABBITMQ__PORT` | `5672` | AMQP port |
| `RECEIVER__RABBITMQ__VIRTUALHOST` | `/` | Virtual host |
| `RECEIVER__RABBITMQ__USERNAME` | `data-receiver` | AMQP user |
| `RECEIVER__RABBITMQ__PASSWORD` | *(secrets file only)* | Never commit — lives in `/etc/data-receiving/secrets` |
| `RECEIVER__RABBITMQ__EXCHANGE` | `sensors.raw` | Topic exchange declared by Wave 1 |
| `RECEIVER__RABBITMQ__CONFIRMTIMEOUTSECONDS` | `5` | Publisher Confirms timeout |

> **Password rule:** `RECEIVER__RABBITMQ__PASSWORD` must never be committed to git. For local dev it can live in `.env` (gitignored). On servers it belongs exclusively in `/etc/data-receiving/secrets` (permissions `640`, owner `root:data-receiving`).

### Consul (required when `OutputMode=rabbitmq`)

| Env var | Wave 1 value | Description |
|---|---|---|
| `RECEIVER__CONSUL__ADDRESS` | `http://204.168.203.32:8500` | Consul agent address |
| `RECEIVER__CONSUL__TOKEN` | *(secrets file only)* | ACL token — never commit |
| `RECEIVER__CONSUL__INSTANCEID` | `data-receiving-01` | Unique ID for this instance (`01` / `02` / `03`) |
| `RECEIVER__CONSUL__HEALTHPORT` | `8080` | Port the embedded health endpoint listens on |
| `RECEIVER__CONSUL__SERVICEVERSION` | `1.0.0` | Service version tag |
| `RECEIVER__CONSUL__ENVIRONMENT` | `prod` | Environment tag (`prod`, `staging`, etc.) |

> **Token rule:** `RECEIVER__CONSUL__TOKEN` must never be committed to git. For local dev it can live in `.env` (gitignored). On servers it belongs exclusively in `/etc/data-receiving/secrets`.

---

## Wave 1 infrastructure

All external services run on a single Wave 1 server. Credentials are stored only in `/etc/data-receiving/secrets` on each deployment host — never in this repository.

| Service | Host | Port | Notes |
|---|---|---|---|
| RabbitMQ AMQP | `204.168.203.32` | `5672` | User: `data-receiver` |
| RabbitMQ Management UI | `204.168.203.32` | `15672` | `http://204.168.203.32:15672` |
| Redis | `204.168.203.32` | `6379` | Password required — include in connection string |
| Consul | `204.168.203.32` | `8500` | Service discovery — token in secrets file |

Verify connectivity before deploying:

```bash
# Redis
nc -z -w3 204.168.203.32 6379 && echo "Redis OK"

# RabbitMQ AMQP
nc -z -w3 204.168.203.32 5672 && echo "RabbitMQ OK"

# RabbitMQ Management (replace <user> and <pass>)
curl -s -u '<user>:<pass>' http://204.168.203.32:15672/api/overview | python3 -m json.tool | grep rabbitmq_version

# Consul (replace <token>)
curl -s -H "X-Consul-Token: <token>" http://204.168.203.32:8500/v1/agent/self | python3 -m json.tool | grep "\"Version\""
```

---

## RabbitMQ topology

The exchange and queues were declared by Wave 1. This service **only publishes** — it never declares or modifies the topology.

| Element | Value |
|---|---|
| Exchange | `sensors.raw` |
| Type | `topic` |
| Durable | `true` |
| Routing key pattern | `sensors.{project_id}.{sensor_type}` |
| Delivery mode | `2` (persistent) |

Routing keys produced by this service:

| Sensor type | Example project | Routing key |
|---|---|---|
| `gnss` | `shangtan` | `sensors.shangtan.gnss` |
| `rainfall` | `shangtan` | `sensors.shangtan.rainfall` |
| `strain` | `shangtan` | `sensors.shangtan.strain` |
| `soil_pressure` | `shangtan` | `sensors.shangtan.soil_pressure` |
| `deep_displacement` | `lingzhan` | `sensors.lingzhan.deep_displacement` |

The `project_id` and `sensor_type` come directly from the parsed envelope — no mapping or transformation is applied.

Inspect live queue state via the management UI or CLI:

```bash
# Messages waiting in a queue (replace <queue>, <user>, <pass>)
curl -s -u '<user>:<pass>' \
  http://204.168.203.32:15672/api/queues/%2F/<queue> \
  | python3 -c "import sys,json; d=json.load(sys.stdin); print('messages:', d['messages'])"
```

---

## Redis buffer

When `OutputMode=rabbitmq` the service writes every parsed envelope to Redis **before** attempting to publish, and deletes it only after the broker confirms receipt (Publisher Confirms). This prevents data loss if RabbitMQ is unavailable.

**Key format:** `sensor_buffer:{message_id}`

**Value:** JSON wrapper stored as a Redis string:

```json
{
  "stored_at": "2026-05-12T14:30:00.123Z",
  "routing_key": "sensors.shangtan.gnss",
  "message_id": "550e8400-e29b-41d4-a716-446655440000",
  "envelope_json": "{...full envelope...}"
}
```

**TTL:** 24 hours. If RabbitMQ stays down longer than the TTL the buffered message expires and is lost.

**RetryWorker** runs every 30 seconds, scans for keys older than 2 minutes, and retries publishing each one. On success it deletes the key. Counts and outcomes are logged at `Information` level.

Inspect the buffer:

```bash
# Count buffered messages (replace <pass>)
redis-cli -h 204.168.203.32 -p 6379 -a '<pass>' --no-auth-warning \
  SCAN 0 MATCH "sensor_buffer:*" COUNT 1000

# Read a specific key
redis-cli -h 204.168.203.32 -p 6379 -a '<pass>' --no-auth-warning \
  GET "sensor_buffer:<message_id>"
```

---

## Consul service registration

When `OutputMode=rabbitmq`, the service registers itself with Consul on startup and deregisters cleanly on shutdown.

**Service name:** `data-receiving`

**Service ID:** `data-receiving-{InstanceId}` (e.g. `data-receiving-data-receiving-01`)

**Tags:** `prod`, `version:1.0.0`, `instance:data-receiving-01` (all configurable)

**Health check:** HTTP GET to `http://localhost:{HealthPort}/health/` every 10 seconds. Returns `200 {"status":"ok"}` once the TCP server is listening, `503 {"status":"starting"}` before that. Consul deregisters the service automatically if it stays critical for 1 minute.

The health endpoint (`HealthEndpoint`) is a separate `BackgroundService` that runs an embedded `HttpListener` on port 8080 — no extra dependency or web framework needed.

Verify registration after deploy:

```bash
# List all registered data-receiving instances
curl -s http://204.168.203.32:8500/v1/catalog/service/data-receiving | python3 -m json.tool

# Check health status on the agent
curl -s http://204.168.203.32:8500/v1/health/service/data-receiving | python3 -m json.tool

# Hit the health endpoint directly on the service host
curl -sv http://<service-host>:8080/health/
```

---

## Deploying to Linux (Ubuntu 24.04)

### Option A — via GitHub Actions (recommended)

Every push to `main` produces a deployable artifact: **`data-receiving-linux-x64`** (self-contained binary + `deploy/` folder, no .NET runtime needed on the server).

1. Go to **Actions → latest green run → Artifacts → `data-receiving-linux-x64`** and download the zip.

2. Copy it to the server:

```bash
scp data-receiving-linux-x64.zip user@your-server:~
```

3. On the server:

```bash
unzip data-receiving-linux-x64.zip -d release
sudo RABBITMQ_PASSWORD="<RabbitMQ password>" \
     REDIS_PASSWORD="<Redis password>" \
     CONSUL_TOKEN="<Consul token>" \
     bash release/deploy/install.sh \
       --instance 01 \
       --redis-host 204.168.203.32 \
       --rabbitmq-host 204.168.203.32 \
       --consul-host 204.168.203.32
```

Replace `--instance 01` with `02` or `03` for the second and third server.

4. Start and verify:

```bash
sudo systemctl start data-receiving
sudo journalctl -u data-receiving -f
curl http://localhost:8081/health/   # port 8080 was in use on Wave 1 server; changed to 8081
```

---

### Option B — build and deploy manually

On your development machine:

> **Important:** build with .NET installed from the **Microsoft package feed**, not
> from snap. A snap-installed `dotnet` hardwires the binary to
> `/snap/core20/current/lib64/ld-linux-x86-64.so.2` as its ELF interpreter —
> that path does not exist on a standard Ubuntu server, causing a cryptic
> `No such file or directory` error when systemd tries to execute the binary.
>
> Verify your dotnet is not from snap:
> ```bash
> which dotnet   # should be /usr/bin/dotnet, not /snap/bin/dotnet
> ```
> If it is from snap, install from Microsoft's feed instead:
> ```bash
> wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb
> sudo dpkg -i packages-microsoft-prod.deb
> sudo apt update && sudo apt install dotnet-sdk-8.0 -y
> ```

```bash
dotnet publish src/DataReceiving/DataReceiving.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:DebugType=none \
  -o publish/

scp -r publish/ deploy/ user@your-server:~/data-receiving-release/
```

On the server:

```bash
cd ~/data-receiving-release
sudo RABBITMQ_PASSWORD="<RabbitMQ password>" \
     REDIS_PASSWORD="<Redis password>" \
     CONSUL_TOKEN="<Consul token>" \
     bash deploy/install.sh \
       --instance 01 \
       --redis-host 204.168.203.32 \
       --rabbitmq-host 204.168.203.32 \
       --consul-host 204.168.203.32
sudo systemctl start data-receiving
```

---

### What the installer does

`deploy/install.sh` (requires root):

| Flag | Env var | Description |
|---|---|---|
| `--instance <id>` | — | Instance ID appended to Consul service ID (`01`/`02`/`03`) |
| `--redis-host <h>` | — | Redis host (default: `localhost`) |
| `--rabbitmq-host <h>` | — | RabbitMQ host (default: `localhost`) |
| `--consul-host <h>` | — | Consul host (default: `localhost`) |
| — | `RABBITMQ_PASSWORD` | **Required** — written to secrets file |
| — | `REDIS_PASSWORD` | **Required** — written to secrets file as full connection string |
| — | `CONSUL_TOKEN` | Optional — appended to secrets file if set |

Steps performed:

1. Creates system user `data-receiving` (no shell, no home).
2. Copies the self-contained binary to `/opt/data-receiving/`.
3. Creates `/var/log/data-receiving/dlx/`.
4. Writes `/etc/data-receiving/secrets` with credentials — permissions `640`, owner `root:data-receiving`.
5. Substitutes host and instance placeholders in `deploy/data-receiving.service` and installs it to `/etc/systemd/system/`.
6. Runs `systemctl daemon-reload`, enables and restarts the service.

The installer is idempotent: running it again updates the binary and unit file without touching secrets.

---

### Secrets file

`/etc/data-receiving/secrets` is the only file that holds credentials. It is created by `install.sh` and never tracked by git:

```
RECEIVER__RABBITMQ__PASSWORD=<RabbitMQ password>
RECEIVER__REDIS__CONNECTIONSTRING=<redis-host>:6379,password=<Redis password>
RECEIVER__CONSUL__TOKEN=<Consul token>
```

The systemd unit reads it via `EnvironmentFile=/etc/data-receiving/secrets`. All other configuration lives in the `Environment=` directives inside the unit file itself (hosts, ports, usernames, exchange, instance ID, etc. — nothing sensitive).

### Firewall

If `ufw` is active, open the TCP listener port and the health endpoint port:

```bash
sudo ufw allow 5000/tcp   # DTU sensors
sudo ufw allow 8080/tcp   # Consul health checks (only from Consul agent)
sudo ufw status
```

---

## Troubleshooting

### `No such file or directory` when systemd starts the binary

The binary executes fine locally but fails on the server with:

```
data-receiving.service: Failed to execute /opt/data-receiving/DataReceiving: No such file or directory
```

**Cause:** the binary was built with a snap-installed `dotnet`, which embeds
`/snap/core20/current/lib64/ld-linux-x86-64.so.2` as the ELF interpreter.
That path does not exist on the server.

**Verify:**
```bash
file /opt/data-receiving/DataReceiving
# look for "interpreter /snap/core20/..." in the output
```

**Fix A (permanent):** rebuild with Microsoft feed `dotnet` (see Option B above).

**Fix B (emergency):** patch the interpreter on the server:
```bash
sudo apt install patchelf -y
sudo patchelf --set-interpreter /lib/x86_64-linux-gnu/ld-linux-x86-64.so.2 \
  /opt/data-receiving/DataReceiving
sudo systemctl start data-receiving
```

---

### Health endpoint port already in use

If the service crashes immediately with `Address already in use` on port 8080,
something else is using that port. Change the health port in the unit file:

```bash
sudo systemctl stop data-receiving
sudo nano /etc/systemd/system/data-receiving.service
# change RECEIVER__CONSUL__HEALTHPORT=8080 to an available port, e.g. 8081
sudo systemctl daemon-reload
sudo systemctl start data-receiving
sudo ufw allow 8081/tcp
```

Update `docs/consul-setup.md` and the `config.yaml` in the e2e tests to match.

---

### Consul shows service registered but `ServicePort` is the health port

The C# service registers `ServicePort = HealthPort` (e.g. 8081), not the TCP
data port (5000). This is a known characteristic of the current implementation.

When configuring consul-template to generate Nginx upstreams, derive the TCP
data port from the health port. For the Wave 1 deployment (health ports
8081/8082/8083, data ports 5000/5001/5002) the offset is always 3081:

```
{{ subtract 3081 .Port }}   → gives the TCP data port
```

See `docs/consul-setup.md` for the full consul-template configuration.

---

## Managing the service

```bash
# Status
sudo systemctl status data-receiving

# Start / stop / restart
sudo systemctl start data-receiving
sudo systemctl stop data-receiving
sudo systemctl restart data-receiving

# Live logs
sudo journalctl -u data-receiving -f

# Last 200 lines
sudo journalctl -u data-receiving -n 200

# Logs since a specific time
sudo journalctl -u data-receiving --since "2026-05-15 14:00:00"

# Enable / disable autostart on boot
sudo systemctl enable data-receiving
sudo systemctl disable data-receiving
```

### Updating the binary

```bash
sudo systemctl stop data-receiving
sudo cp -r publish/. /opt/data-receiving/
sudo chown -R data-receiving:data-receiving /opt/data-receiving/
sudo chmod +x /opt/data-receiving/DataReceiving
sudo systemctl start data-receiving
```

Secrets and unit file are not touched.

### Monitoring Redis buffer after deploy

```bash
# Should reach 0 within a few seconds if RabbitMQ is reachable
redis-cli -h 204.168.203.32 -p 6379 -a '<pass>' --no-auth-warning \
  SCAN 0 MATCH "sensor_buffer:*" COUNT 1000 | grep -c "sensor_buffer"
```

---

## Running multiple instances

The paper (Bai et al., Table 2) deploys three instances across heterogeneous hardware. The normal deployment is **one instance per physical server** — each server runs `install.sh` with its own `--instance` ID and all instances publish to the same RabbitMQ exchange.

```bash
# Server 1
sudo ... bash install.sh --instance 01 --redis-host 204.168.203.32 ...

# Server 2
sudo ... bash install.sh --instance 02 --redis-host 204.168.203.32 ...

# Server 3
sudo ... bash install.sh --instance 03 --redis-host 204.168.203.32 ...
```

Each instance registers in Consul under a unique service ID (`data-receiving-data-receiving-01`, `02`, `03`), so Consul sees all three.

### Two instances on the same host (testing only)

Three things conflict and must be different: **TCP port**, **health endpoint port**, and **Consul instance ID**.

1. Install instance 01 normally (TCP `5000`, health `8080`).

2. Build a second unit file for instance 02:

```bash
sudo cp /etc/systemd/system/data-receiving.service \
        /etc/systemd/system/data-receiving-2.service
sudo sed -i \
  -e 's|ExecStart=/opt/data-receiving/|ExecStart=/opt/data-receiving-2/|' \
  -e 's|WorkingDirectory=/opt/data-receiving|WorkingDirectory=/opt/data-receiving-2|' \
  -e 's|SyslogIdentifier=data-receiving|SyslogIdentifier=data-receiving-2|' \
  -e 's|RECEIVER__PORT=5000|RECEIVER__PORT=5001|' \
  -e 's|RECEIVER__CONSUL__HEALTHPORT=8080|RECEIVER__CONSUL__HEALTHPORT=8081|' \
  -e 's|RECEIVER__CONSUL__INSTANCEID=data-receiving-01|RECEIVER__CONSUL__INSTANCEID=data-receiving-02|' \
  /etc/systemd/system/data-receiving-2.service
```

3. Copy the binary and start:

```bash
sudo cp -r /opt/data-receiving/. /opt/data-receiving-2/
sudo chown -R data-receiving:data-receiving /opt/data-receiving-2
sudo systemctl daemon-reload
sudo systemctl enable --now data-receiving-2
```

4. Open the extra ports:

```bash
sudo ufw allow 5001/tcp
sudo ufw allow 8081/tcp
```

---

## Running with the Python sender

```bash
# Normal run — 100 frames
python sender/sender.py --host 127.0.0.1 --port 5000 --count 100

# Corrupt-CRC test — ACK 0x01, frame goes to dlx/
python sender/sender.py --corrupt crc

# Schema violation test — ACK 0x03
python sender/sender.py --corrupt schema

# Unknown type test — ACK 0x04
python sender/sender.py --corrupt type

# Throughput test — 1000 frames, no rate limit
python sender/sender.py --count 1000 --rate 0
```

Inspect rejected frames:

```bash
ls /var/log/data-receiving/dlx/
cat /var/log/data-receiving/dlx/<filename>.json
```

---

## Architecture

```
DTU sender (TCP)
    │
    ▼
FrameReader            reads length-prefixed binary frames from the TCP stream
    │
    ▼
DtuPacketParser
    ├── FrameValidator      sync bytes / VER / TYPE / LEN / CRC-16
    ├── EnvelopeDecoder     message_id / project_id / sensor_id / timestamp / sampling_interval
    ├── *PayloadDecoder     one per sensor type (GNSS / Rainfall / Strain / Soil / Deep)
    └── SchemaValidator     Draft 2020-12 against embedded JSON schemas
    │
    ▼
RabbitMqSink  (when OutputMode=rabbitmq)
    │
    ├─1─► RedisMessageBuffer.SetAsync     persist to Redis before touching RabbitMQ
    │         key: sensor_buffer:{message_id}   TTL: 24h
    │
    ├─2─► RabbitMqPublisher.PublishJsonAsync
    │         exchange: sensors.raw
    │         routing key: sensors.{project_id}.{sensor_type}
    │         delivery_mode: 2 (persistent)
    │         Publisher Confirms — waits for broker ACK
    │
    └─3─► RedisMessageBuffer.DeleteAsync  only if broker confirmed
              if publish failed: message stays in Redis for RetryWorker
    │
    ▼
RetryWorker  (BackgroundService, every 30s)
    │  scans sensor_buffer:* keys older than 2 min
    │  retries PublishJsonAsync for each
    └──────────────────────────────────────────────

ACK → DTU sender
LocalDlxWriter → /var/log/data-receiving/dlx/*.json   (rejected frames only)

Side services (when OutputMode=rabbitmq):

HealthEndpoint  (BackgroundService, HttpListener on :8080)
    │  GET /health/ → 200 {"status":"ok"}   when TCP server is listening
    │              → 503 {"status":"starting"} while starting up
    └── read-only access to TcpServerStatus singleton

ConsulRegistration  (IHostedService)
    │  StartAsync → Agent.ServiceRegister(data-receiving, tags, HTTP health check)
    └── StopAsync  → Agent.ServiceDeregister(service-id)
```

Other output modes (`StdoutSink`, `FileSink`, `NullSink`) bypass Redis and RabbitMQ entirely and are used for local development and testing.

The parser (`IDtuPacketParser.Parse`) takes `ReadOnlySpan<byte>` and has no dependency on sockets or external services. All 62 parser tests use hardcoded byte arrays.

JSON schemas (`envelope.schema.json` + five `payload-*.schema.json`) are compiled into the assembly as embedded resources — no extra files needed at runtime.

---

## Sensor types

| TYPE byte | `sensor_type` | Schema |
|---|---|---|
| `0x01` | `gnss` | `payload-gnss.schema.json` |
| `0x02` | `rainfall` | `payload-rainfall.schema.json` |
| `0x03` | `strain` | `payload-strain.schema.json` |
| `0x04` | `soil_pressure` | `payload-soil-pressure.schema.json` |
| `0x05` | `deep_displacement` | `payload-deep-displacement.schema.json` |

---

## Dead letter format

Any frame that fails validation is written to `dlx/<timestamp>_<uuid>.json` and never blocks the pipeline:

```json
{
  "received_at": "2026-05-12T14:30:00.123Z",
  "raw_frame_hex": "AA55010100...",
  "stage": "Crc",
  "error_message": "CRC mismatch: expected 0x29B1 got 0xFFFF",
  "ack_status": 1
}
```

| `ack_status` | `stage` | Meaning |
|---|---|---|
| `1` | `Crc` | CRC mismatch |
| `2` | `SyncBytes` / `Version` / `Length` / `HeaderDecode` / `PayloadDecode` | Malformed frame |
| `3` | `SchemaValidation` | Frame parsed but JSON schema failed |
| `4` | `SensorType` | Unknown sensor type byte |
