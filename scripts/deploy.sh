#!/usr/bin/env bash
set -euo pipefail
SVC=utilities
PORT=5006
REPO=/opt/blocks/code/blocks-utilities
ENVF=/opt/blocks/secrets/.env
OUT="$REPO/builds"
git -C "$REPO" fetch --prune origin inception
git -C "$REPO" checkout -f inception
git -C "$REPO" reset --hard origin/inception
if [ -d "$REPO/client" ]; then
  ( cd "$REPO/client" && npm install --no-audit --no-fund && npm run build )
fi
dotnet publish "$REPO/server/Api/Api.csproj" -c Release -o "$OUT/api" --nologo
dotnet publish "$REPO/server/Worker/Worker.csproj" -c Release -o "$OUT/worker" --nologo
cat >/etc/systemd/system/blocks-$SVC-api.service <<UNIT
[Unit]
Description=blocks-$SVC api
After=network.target
[Service]
WorkingDirectory=$OUT/api
EnvironmentFile=$ENVF
Environment=ASPNETCORE_ENVIRONMENT=dev
Environment=DOTNET_ENVIRONMENT=dev
Environment=ASPNETCORE_URLS=http://127.0.0.1:$PORT
ExecStart=/usr/bin/dotnet $OUT/api/Api.dll
Restart=always
RestartSec=5
SyslogIdentifier=blocks-$SVC-api
[Install]
WantedBy=multi-user.target
UNIT
cat >/etc/systemd/system/blocks-$SVC-worker.service <<UNIT
[Unit]
Description=blocks-$SVC worker
After=network.target
[Service]
WorkingDirectory=$OUT/worker
EnvironmentFile=$ENVF
Environment=ASPNETCORE_ENVIRONMENT=dev
Environment=DOTNET_ENVIRONMENT=dev
ExecStart=/usr/bin/dotnet $OUT/worker/Worker.dll
Restart=always
RestartSec=5
SyslogIdentifier=blocks-$SVC-worker
[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl enable blocks-$SVC-api blocks-$SVC-worker >/dev/null 2>&1
systemctl restart blocks-$SVC-api blocks-$SVC-worker
# graphify knowledge graph - best-effort, runs after services are up.
# Wrapped so nothing here can fail the deploy. Delete this block to disable.
(
  set +euo pipefail
  export PATH="$HOME/.local/bin:$PATH"
  if ! command -v graphify >/dev/null 2>&1; then
    command -v pipx >/dev/null 2>&1 || {
      export DEBIAN_FRONTEND=noninteractive
      apt-get update -qq && apt-get install -y -qq pipx
    }
    pipx install graphifyy
  fi
  if command -v graphify >/dev/null 2>&1; then
    cd "$REPO" || exit 0
    graphify install --platform codex --project
    graphify extract "$REPO" --code-only
  else
    echo "graphify unavailable - skipping graph build (deploy unaffected)"
  fi
) </dev/null >/tmp/graphify-$SVC.log 2>&1 || true
echo "graphify: see /tmp/graphify-$SVC.log"
