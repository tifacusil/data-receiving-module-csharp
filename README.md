# Data Receiving Module (C#)

Parser microservice for the landslide monitoring system described in Bai et al. (2020). Receives binary DTU frames over TCP, validates them, and produces JSON envelopes conforming to the shared contract.

Implements **Card 2A1** (parser + TCP server). RabbitMQ publishing (2A2) and Consul registration (2A3) are not included here.

## Build

```bash
dotnet build DataReceiving.sln
```

## Test

```bash
dotnet test DataReceiving.sln
```

All 62 tests (42 unit + 10 integration) must pass. The CRC test vector `Crc16Ccitt.Compute("123456789") == 0x29B1` is a shared contract with the Python sender.

## Run

```bash
cd src/DataReceiving
dotnet run
```

Or as a compiled binary:

```bash
dotnet publish -c Release -r linux-x64 --self-contained true
./bin/Release/net8.0/linux-x64/publish/DataReceiving
```

## Configuration

`appsettings.json` (environment variables override with `RECEIVER__` prefix):

```json
{
  "Receiver": {
    "Port": 5000,
    "DlxPath": "./dlx",
    "OutputMode": "stdout",
    "OutputFilePath": "./output"
  }
}
```

| Key | Default | Description |
|---|---|---|
| `Port` | `5000` | TCP listen port |
| `DlxPath` | `./dlx` | Directory for rejected frames |
| `OutputMode` | `stdout` | `stdout` / `file` / `null` |
| `OutputFilePath` | `./output` | Output directory when `OutputMode=file` |

Override with environment variables:

```bash
RECEIVER__PORT=5001 RECEIVER__OUTPUTMODE=file ./DataReceiving
```

## Running with the Python sender

```bash
# Terminal 1: start the receiver
dotnet run --project src/DataReceiving

# Terminal 2: run the sender
python sender/sender.py --host 127.0.0.1 --port 5000 --count 100

# Corrupt-CRC test (CA-3)
python sender/sender.py --corrupt crc

# Schema violation test (CA-4)
python sender/sender.py --corrupt schema

# Unknown type test (CA-5)
python sender/sender.py --corrupt type

# Throughput test (CA-8)
python sender/sender.py --count 1000 --rate 0 --output-mode file
```

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
            └── LocalDlxWriter   rejected frames → ./dlx/*.json
```

The parser (`IDtuPacketParser.Parse`) is transport-agnostic — it takes `ReadOnlySpan<byte>` and returns a `ParseResult`. All tests use hardcoded bytes without any network mocks.

## JSON Schema validation

The six schemas (`envelope.schema.json` + five `payload-*.schema.json`) from `contracts/schemas/` are compiled into the assembly as embedded resources. No files need to be deployed alongside the binary.

## Sensor types

| TYPE byte | sensor_type | Schema |
|---|---|---|
| `0x01` | `gnss` | `payload-gnss.schema.json` |
| `0x02` | `rainfall` | `payload-rainfall.schema.json` |
| `0x03` | `strain` | `payload-strain.schema.json` |
| `0x04` | `soil_pressure` | `payload-soil-pressure.schema.json` |
| `0x05` | `deep_displacement` | `payload-deep-displacement.schema.json` |

## Dead letter format

Rejected frames are written to `./dlx/<timestamp>_<uuid>.json`:

```json
{
  "received_at": "2026-05-12T14:30:00.123Z",
  "raw_frame_hex": "AA55010100...",
  "stage": "Crc",
  "error_message": "CRC mismatch: expected 0x29B1 got 0xFFFF",
  "ack_status": 1
}
```
