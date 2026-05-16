#!/usr/bin/env bash
# install.sh — installs Data Receiving on Ubuntu 24.04 LTS.
# Usage: sudo RABBITMQ_PASSWORD=<pass> REDIS_HOST=<host> RABBITMQ_HOST=<host> bash install.sh
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

# --- Required variables ---
: "${RABBITMQ_PASSWORD:?Set RABBITMQ_PASSWORD before running}"
REDIS_HOST="${REDIS_HOST:-localhost}"
RABBITMQ_HOST="${RABBITMQ_HOST:-localhost}"

echo "==> Installing Data Receiving"
echo "    Redis:    $REDIS_HOST"
echo "    RabbitMQ: $RABBITMQ_HOST"

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

# --- Secrets file (password only, strict permissions) ---
cat > "$SECRETS_FILE" <<EOF
RECEIVER__RABBITMQ__PASSWORD=${RABBITMQ_PASSWORD}
EOF
chown root:"$SERVICE" "$SECRETS_FILE"
chmod 640 "$SECRETS_FILE"
echo "==> Secrets written to $SECRETS_FILE"

# --- Systemd unit (substitute host placeholders) ---
sed \
  -e "s|<redis-host>|${REDIS_HOST}|g" \
  -e "s|<rabbitmq-host>|${RABBITMQ_HOST}|g" \
  "$SCRIPT_DIR/data-receiving.service" > "$SERVICE_FILE"

chmod 644 "$SERVICE_FILE"
systemctl daemon-reload
systemctl enable "$SERVICE"
systemctl restart "$SERVICE"

echo ""
echo "==> Done. Verify with:"
echo "    systemctl status $SERVICE"
echo "    journalctl -u $SERVICE -f"
