#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

CLIENT_DIR="$SCRIPT_DIR/client"
E2E_DIR="$SCRIPT_DIR/e2e"
API_PROJECT="$SCRIPT_DIR/server/Api/Api.csproj"
WORKER_PROJECT="$SCRIPT_DIR/server/Worker/Worker.csproj"
WWWROOT_DIR="$SCRIPT_DIR/server/Api/wwwroot"
SOLUTION="$SCRIPT_DIR/server/Blocks.slnx"

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

Tests:
  -tf, --test-fe    Frontend unit tests (client/: build + vitest)
  -te, --test-e2e   End-to-end tests (e2e/: playwright)
  -tb, --test-be    Backend unit tests (server/: clean + build + dotnet test)
  -ta, --test-all   Frontend unit + backend unit + e2e

Env:
  SKIP_BUILD=1      Skip the client build step in --test-fe

Examples:
  $0 -a
  $0 -b
  $0 -f
  $0 -k
  $0 -tf
  $0 -tb
  $0 -te
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

# ---------- TESTS ----------
ensure_node_modules() {
    local dir=$1
    if [ ! -d "$dir/node_modules" ]; then
        echo "Installing dependencies in $(basename "$dir")..."
        npm --prefix "$dir" clean-install
    fi
}

# Vitest does not read dist/, so the build is only a TypeScript gate.
# Skip it with SKIP_BUILD=1 for a faster loop.
test_frontend() {
    echo "=== Frontend unit tests ==="

    ensure_node_modules "$CLIENT_DIR"

    if [ "${SKIP_BUILD:-0}" = "1" ]; then
        echo "SKIP_BUILD=1 — skipping client build."
    else
        npm --prefix "$CLIENT_DIR" run build
    fi

    npm --prefix "$CLIENT_DIR" run test
}

# Playwright starts the app itself (webServer: run.sh -b) and needs e2e/.env.e2e.
test_e2e() {
    echo "=== E2E tests ==="

    if [ ! -f "$E2E_DIR/.env.e2e" ]; then
        echo "Missing $E2E_DIR/.env.e2e — copy .env.e2e.example and set E2E_BASE_URL + credentials."
        exit 1
    fi

    ensure_node_modules "$E2E_DIR"

    (cd "$E2E_DIR" && npx playwright install --no-shell chromium)
    npm --prefix "$E2E_DIR" run test
}

test_backend() {
    echo "=== Backend unit tests ==="

    dotnet clean "$SOLUTION"
    dotnet build "$SOLUTION"
    dotnet test "$SOLUTION" --no-build
}

test_all() {
    test_frontend
    test_backend
    test_e2e
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

    -tf|--test-fe)
        test_frontend
        ;;

    -te|--test-e2e)
        test_e2e
        ;;

    -tb|--test-be)
        test_backend
        ;;

    -ta|--test-all)
        test_all
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