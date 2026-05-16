#!/usr/bin/env bash
# install.sh — installs Data Receiving on Ubuntu 24.04 LTS.
# Usage: sudo RABBITMQ_PASSWORD=<pass> bash install.sh --instance 01 [--redis-host <h>] [--rabbitmq-host <h>] [--consul-host <h>]
set -euo pipefail

INSTALL_DIR="/opt/data-receiving"
LOG_DIR="/var/log/data-receiving"
SECRETS_DIR="/etc/data-receiving"
SECRETS_FILE="$SECRETS_DIR/secrets"
SERVICE="data-receiving"
SERVICE_FILE="/etc/systemd/system/$SERVICE.service"

# --- Verify root ---
if [[ $EUID -ne 0 ]]; then
  echo "ERROR: run with sudo"
  exit 1
fi

# --- Required env vars ---
: "${RABBITMQ_PASSWORD:?Set RABBITMQ_PASSWORD before running}"

# --- Default flag values ---
INSTANCE_ID=""
REDIS_HOST="localhost"
RABBITMQ_HOST="localhost"
CONSUL_HOST="localhost"

# --- Parse flags ---
while [[ $# -gt 0 ]]; do
  case "$1" in
    --instance)    INSTANCE_ID="$2";   shift 2 ;;
    --redis-host)  REDIS_HOST="$2";    shift 2 ;;
    --rabbitmq-host) RABBITMQ_HOST="$2"; shift 2 ;;
    --consul-host) CONSUL_HOST="$2";   shift 2 ;;
    *) echo "Unknown argument: $1"; exit 1 ;;
  esac
done

if [[ -z "$INSTANCE_ID" ]]; then
  echo "ERROR: --instance <id> is required (e.g. --instance 01)"
  exit 1
fi

echo "==> Installing Data Receiving"
echo "    Instance: $INSTANCE_ID"
echo "    Redis:    $REDIS_HOST"
echo "    RabbitMQ: $RABBITMQ_HOST"
echo "    Consul:   $CONSUL_HOST"

# --- System user ---
if ! id "$SERVICE" &>/dev/null; then
  useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE"
  echo "==> Created system user $SERVICE"
fi

# --- Directories ---
mkdir -p "$INSTALL_DIR" "$LOG_DIR/dlx" "$SECRETS_DIR"
chown -R "$SERVICE:$SERVICE" "$INSTALL_DIR" "$LOG_DIR"
chmod 750 "$INSTALL_DIR" "$LOG_DIR"

# --- Binary ---
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BINARY_SRC="$SCRIPT_DIR/../publish"

if [[ ! -f "$BINARY_SRC/DataReceiving" ]]; then
  echo "ERROR: binary not found at $BINARY_SRC/DataReceiving"
  echo "  Build first: dotnet publish src/DataReceiving/DataReceiving.csproj -c Release -r linux-x64 --self-contained true -o publish/"
  exit 1
fi

cp -r "$BINARY_SRC/." "$INSTALL_DIR/"
chown -R "$SERVICE:$SERVICE" "$INSTALL_DIR"
chmod +x "$INSTALL_DIR/DataReceiving"
echo "==> Binary installed at $INSTALL_DIR"

# --- Secrets file (passwords/tokens only, strict permissions) ---
cat > "$SECRETS_FILE" <<EOF
RECEIVER__RABBITMQ__PASSWORD=${RABBITMQ_PASSWORD}
EOF

# Append optional secrets
if [[ -n "${REDIS_PASSWORD:-}" ]]; then
  echo "RECEIVER__REDIS__CONNECTIONSTRING=${REDIS_HOST}:6379,password=${REDIS_PASSWORD}" >> "$SECRETS_FILE"
fi
if [[ -n "${CONSUL_TOKEN:-}" ]]; then
  echo "RECEIVER__CONSUL__TOKEN=${CONSUL_TOKEN}" >> "$SECRETS_FILE"
fi

chown root:"$SERVICE" "$SECRETS_FILE"
chmod 640 "$SECRETS_FILE"
echo "==> Secrets written to $SECRETS_FILE"

# --- Systemd unit (substitute host and instance placeholders) ---
sed \
  -e "s|<redis-host>|${REDIS_HOST}|g" \
  -e "s|<rabbitmq-host>|${RABBITMQ_HOST}|g" \
  -e "s|<consul-host>|${CONSUL_HOST}|g" \
  -e "s|<instance-id>|data-receiving-${INSTANCE_ID}|g" \
  "$SCRIPT_DIR/data-receiving.service" > "$SERVICE_FILE"

chmod 644 "$SERVICE_FILE"
systemctl daemon-reload
systemctl enable "$SERVICE"
systemctl restart "$SERVICE"

echo ""
echo "==> Done. Verify with:"
echo "    systemctl status $SERVICE"
echo "    journalctl -u $SERVICE -f"
echo "    curl http://localhost:8080/health/"
echo "    consul catalog services | grep data-receiving"
