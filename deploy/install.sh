#!/usr/bin/env bash
set -euo pipefail

DEPLOY_DIR=/opt/data-receiving
SERVICE=data-receiving
BINARY_DIR="$(cd "$(dirname "$0")/../publish" && pwd)"

echo "==> Creating system user"
id "$SERVICE" &>/dev/null || useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE"

echo "==> Installing binary to $DEPLOY_DIR"
mkdir -p "$DEPLOY_DIR"/{dlx,output}
cp -r "$BINARY_DIR"/. "$DEPLOY_DIR"/
chown -R "$SERVICE":"$SERVICE" "$DEPLOY_DIR"
chmod 750 "$DEPLOY_DIR"
chmod 755 "$DEPLOY_DIR/DataReceiving"

echo "==> Installing .env (skipped if already exists)"
if [ ! -f "$DEPLOY_DIR/.env" ]; then
    cp "$(dirname "$0")/.env.example" "$DEPLOY_DIR/.env"
    chmod 640 "$DEPLOY_DIR/.env"
    chown "$SERVICE":"$SERVICE" "$DEPLOY_DIR/.env"
    echo "    Created $DEPLOY_DIR/.env — edit it before starting the service"
fi

echo "==> Installing systemd unit"
cp "$(dirname "$0")/$SERVICE.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable "$SERVICE"

echo ""
echo "Done. Next steps:"
echo "  1. Edit $DEPLOY_DIR/.env"
echo "  2. sudo systemctl start $SERVICE"
echo "  3. sudo journalctl -u $SERVICE -f"
