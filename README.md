# Data Receiving Module (C#)

Parser microservice for the landslide monitoring system described in Bai et al. (2020). Receives binary DTU frames over TCP, validates them, and produces JSON envelopes conforming to the shared contract.

Implements **Card 2A1** (parser + TCP server). RabbitMQ publishing (2A2) and Consul registration (2A3) are not included here.

---

## Table of contents

1. [Local development](#local-development)
2. [Configuration](#configuration)
3. [Deploying to Linux (Ubuntu 24.04)](#deploying-to-linux-ubuntu-2404)
4. [Managing the service](#managing-the-service)
5. [Running multiple instances](#running-multiple-instances)
6. [Running with the Python sender](#running-with-the-python-sender)
7. [Architecture](#architecture)
8. [Sensor types](#sensor-types)
9. [Dead letter format](#dead-letter-format)

---

## Local development

**Requirements:** .NET 8 SDK (`dotnet --version` should show `8.x.x`).

```bash
# Build
dotnet build DataReceiving.sln

# Test (62 tests — must all pass)
dotnet test DataReceiving.sln

# Run locally
dotnet run --project src/DataReceiving
```

---

## Configuration

All settings live in `appsettings.json` and can be overridden by environment variables. The convention is `RECEIVER__` prefix with double underscores as section separators (`RECEIVER__REDIS__CONNECTIONSTRING`, etc.). Environment variables always win over the file.

### Core settings

| Env var | Default | Description |
|---|---|---|
| `RECEIVER__PORT` | `5000` | TCP port the server listens on |
| `RECEIVER__DLXPATH` | `./dlx` | Directory where rejected frames are written |
| `RECEIVER__OUTPUTMODE` | `stdout` | `stdout` · `file` · `null` · `rabbitmq` |
| `RECEIVER__OUTPUTFILEPATH` | `./output` | Directory for JSON files when `OutputMode=file` |

### Redis (required when `OutputMode=rabbitmq`)

| Env var | Wave 1 value | Description |
|---|---|---|
| `RECEIVER__REDIS__CONNECTIONSTRING` | `204.168.203.32:6379,password=<Redis password>` | StackExchange.Redis connection string |
| `RECEIVER__REDIS__BUFFERTLTHOURS` | `24` | How long a message stays buffered if RabbitMQ is down |
| `RECEIVER__REDIS__RETRYINTERVALSECONDS` | `30` | How often RetryWorker scans for unsent messages |
| `RECEIVER__REDIS__RETRYMINAGEMINUTES` | `2` | Minimum age before RetryWorker retries a buffered message |

### RabbitMQ (required when `OutputMode=rabbitmq`)

| Env var | Wave 1 value | Description |
|---|---|---|
| `RECEIVER__RABBITMQ__HOST` | `204.168.203.32` | Broker hostname or IP |
| `RECEIVER__RABBITMQ__PORT` | `5672` | AMQP port |
| `RECEIVER__RABBITMQ__VIRTUALHOST` | `/` | Virtual host |
| `RECEIVER__RABBITMQ__USERNAME` | `data-receiver` | AMQP user |
| `RECEIVER__RABBITMQ__PASSWORD` | *(secrets file)* | Never commit — set in `/etc/data-receiving/secrets` |
| `RECEIVER__RABBITMQ__EXCHANGE` | `sensors.raw` | Topic exchange (declared by Wave 1) |
| `RECEIVER__RABBITMQ__CONFIRMTIMEOUTSECONDS` | `5` | Timeout for Publisher Confirms |

> **Password rule:** `RECEIVER__RABBITMQ__PASSWORD` must never go into `.env` or any file tracked by git. It belongs exclusively in `/etc/data-receiving/secrets` (permissions `640`, owner `root:data-receiving`).

---

## Deploying to Linux (Ubuntu 24.04)

### Option A — via GitHub Actions (recommended)

Every push to `main` produces a deployable artifact in the Actions tab: **`data-receiving-linux-x64`**. It contains a self-contained binary (no .NET runtime needed on the server) and the `deploy/` folder.

1. Go to **Actions → latest green run → Artifacts → `data-receiving-linux-x64`** and download the zip.

2. Copy it to the server:

```bash
scp data-receiving-linux-x64.zip user@your-server:~
```

3. On the server:

```bash
unzip data-receiving-linux-x64.zip -d release
sudo bash release/deploy/install.sh
```

4. Edit the environment file with your values:

```bash
sudo nano /opt/data-receiving/.env
```

5. Start:

```bash
sudo systemctl start data-receiving
sudo journalctl -u data-receiving -f
```

---

### Option B — build and deploy manually

On your development machine:

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
sudo bash ~/data-receiving-release/deploy/install.sh
sudo nano /opt/data-receiving/.env
sudo systemctl start data-receiving
```

---

### What the installer does

`deploy/install.sh` performs these steps (requires root):

1. Creates a dedicated system user `data-receiving` with no login shell.
2. Copies the binary and deploy files to `/opt/data-receiving/`.
3. Creates `/opt/data-receiving/dlx/` and `/opt/data-receiving/output/`.
4. Copies `deploy/.env.example` to `/opt/data-receiving/.env` if no `.env` exists yet.
5. Installs `deploy/data-receiving.service` into `/etc/systemd/system/` and enables it (starts on boot).

The installer is idempotent: running it again on an existing installation updates the binary and unit file without touching your `.env`.

---

### The .env file

`/opt/data-receiving/.env` is the only file you need to edit after installation. It uses plain `KEY=VALUE` syntax — no quotes needed for simple values.

```bash
RECEIVER__PORT=5000
RECEIVER__DLXPATH=/opt/data-receiving/dlx
RECEIVER__OUTPUTMODE=stdout
RECEIVER__OUTPUTFILEPATH=/opt/data-receiving/output
```

The file is owned by the `data-receiving` user and readable only by that user and root (`chmod 640`). Never commit it to git — use `deploy/.env.example` as the template.

---

### Firewall

If `ufw` is active, open the TCP port the service listens on:

```bash
sudo ufw allow 5000/tcp
sudo ufw status
```

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
# Build new artifact, copy to server, then:
sudo systemctl stop data-receiving
sudo cp DataReceiving /opt/data-receiving/DataReceiving
sudo systemctl start data-receiving
```

The `.env` is not touched during updates.

---

## Running multiple instances

The paper (Bai et al., Table 2) deploys three instances across heterogeneous hardware. Each instance is an independent process on its own host, listening on the same port. A load balancer or the DTU itself distributes connections across hosts.

To run two instances on the **same host** (e.g. for testing):

1. Copy the service file:

```bash
sudo cp /etc/systemd/system/data-receiving.service \
        /etc/systemd/system/data-receiving-2.service
```

2. Edit `data-receiving-2.service` — change `WorkingDirectory`, `EnvironmentFile`, and `SyslogIdentifier`:

```ini
WorkingDirectory=/opt/data-receiving-2
EnvironmentFile=/opt/data-receiving-2/.env
SyslogIdentifier=data-receiving-2
```

3. Create the second directory, copy the binary, write a `.env` with a different port:

```bash
sudo mkdir -p /opt/data-receiving-2/{dlx,output}
sudo cp /opt/data-receiving/DataReceiving /opt/data-receiving-2/
echo "RECEIVER__PORT=5001" | sudo tee /opt/data-receiving-2/.env
sudo chown -R data-receiving:data-receiving /opt/data-receiving-2
```

4. Enable and start:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now data-receiving-2
```

---

## Running with the Python sender

```bash
# Normal run — 100 frames
python sender/sender.py --host 127.0.0.1 --port 5000 --count 100

# Corrupt-CRC test (CA-3) — ACK 0x01, frame goes to dlx/
python sender/sender.py --corrupt crc

# Schema violation test (CA-4) — ACK 0x03
python sender/sender.py --corrupt schema

# Unknown type test (CA-5) — ACK 0x04
python sender/sender.py --corrupt type

# Throughput test (CA-8) — 1000 frames, no rate limit
python sender/sender.py --count 1000 --rate 0
```

Inspect rejected frames:

```bash
ls /opt/data-receiving/dlx/
cat /opt/data-receiving/dlx/<filename>.json
```

---

## Architecture

```
TCP stream
    └── FrameReader         reads length-prefixed binary frames
            └── DtuPacketParser
                    ├── FrameValidator    sync bytes / VER / TYPE / LEN / CRC
                    ├── EnvelopeDecoder   MSG_ID / project_id / sensor_id / timestamp / sampling_interval
                    ├── *PayloadDecoder   one per sensor type (GNSS / Rainfall / Strain / Soil / Deep)
                    └── SchemaValidator   Draft 2020-12 against embedded JSON schemas
            └── ISink        StdoutSink | FileSink | NullSink
            └── LocalDlxWriter   rejected frames → dlx/*.json
```

The parser (`IDtuPacketParser.Parse`) takes `ReadOnlySpan<byte>` and has no dependency on sockets. All 62 tests use hardcoded byte arrays with no network mocks.

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
