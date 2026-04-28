#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

CLIENT_DIR="$SCRIPT_DIR/client"
API_PROJECT="$SCRIPT_DIR/server/Api/Api.csproj"
WORKER_PROJECT="$SCRIPT_DIR/server/Worker/Worker.csproj"
WWWROOT_DIR="$SCRIPT_DIR/server/Api/wwwroot"

API_PORT=5000
FRONTEND_PORT=4000

API_PID=""
WORKER_PID=""

usage() {
cat <<EOF
Usage: $0 [OPTION]

Options:
  -a, --all         Build frontend + run API + Worker
  -b, --backend     Run .NET API
  -w, --worker      Run .NET Worker
  -f, --frontend    Run frontend dev server
  -k, --kill-port   Kill API port ($API_PORT)
  -n, --npm         Run npm command inside client/
  -h, --help        Show help

Examples:
  $0 -a
  $0 -b
  $0 -f
  $0 -k
EOF
exit 1
}

# ---------- PORT CLEANUP ----------
free_port() {
    local PORT=$1

    if command -v lsof >/dev/null 2>&1; then
        local pids
        pids="$(lsof -tiTCP:$PORT -sTCP:LISTEN || true)"

        if [ -n "$pids" ]; then
            echo "Port $PORT in use by: $pids — killing..."
            for pid in $pids; do
                kill "$pid" 2>/dev/null || true
            done
            sleep 1
        fi
    else
        local pids
        pids="$(netstat -ano 2>/dev/null | grep ":$PORT" | awk '{print $5}' | sort -u || true)"

        if [ -n "$pids" ]; then
            echo "Port $PORT in use by: $pids — killing..."
            for pid in $pids; do
                taskkill //PID "$pid" //F >/dev/null 2>&1 || true
            done
        fi
    fi
}

# ---------- CLEANUP ----------
cleanup() {
    echo "Shutting down..."

    [ -n "${API_PID:-}" ] && kill "$API_PID" 2>/dev/null || true
    [ -n "${WORKER_PID:-}" ] && kill "$WORKER_PID" 2>/dev/null || true
}

trap cleanup EXIT INT TERM

# ---------- FRONTEND ----------
run_frontend() {
    echo "Starting frontend..."

    if [ ! -d "$CLIENT_DIR/node_modules" ]; then
        echo "Installing dependencies..."
        npm --prefix "$CLIENT_DIR" install
    fi

    free_port $FRONTEND_PORT

    npm --prefix "$CLIENT_DIR" run dev
}

build_frontend() {
    echo "Building frontend..."

    npm --prefix "$CLIENT_DIR" install
    npm --prefix "$CLIENT_DIR" run build

    mkdir -p "$WWWROOT_DIR"

    if [ -d "$CLIENT_DIR/dist" ]; then
        echo "Syncing dist → wwwroot..."
        rsync -a --delete "$CLIENT_DIR/dist/" "$WWWROOT_DIR/"
    fi
}

# ---------- BACKEND ----------
run_backend() {
    echo "Running .NET API on port $API_PORT..."
    dotnet run --project "$API_PROJECT"
}

run_worker() {
    echo "Running .NET Worker..."
    dotnet run --project "$WORKER_PROJECT"
}

# ---------- MAIN ----------
if [ $# -eq 0 ]; then
    usage
fi

case "$1" in

    -k|--kill-port)
        free_port $API_PORT
        echo "Port $API_PORT cleared."
        ;;

    -f|--frontend)
        run_frontend
        ;;

    -b|--backend)
        free_port $API_PORT
        (cd "$SCRIPT_DIR" && run_backend) &
        API_PID=$!
        wait $API_PID
        ;;

    -w|--worker)
        (cd "$SCRIPT_DIR" && run_worker)
        ;;

    -a|--all)
        free_port $API_PORT

        build_frontend

        echo "Starting services..."

        (cd "$SCRIPT_DIR" && run_backend) &
        API_PID=$!

        (cd "$SCRIPT_DIR" && run_worker) &
        WORKER_PID=$!

        wait $API_PID $WORKER_PID
        ;;

    -n|--npm)
        shift
        [ $# -eq 0 ] && echo "Usage: $0 -n <args>" && exit 1
        npm --prefix "$CLIENT_DIR" "$@"
        ;;

    -h|--help)
        usage
        ;;

    *)
        usage
        ;;

esac