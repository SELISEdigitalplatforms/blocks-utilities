#!/usr/bin/env bash
# =============================================================================
#  Seeds the blocks-utilities endpoint permissions into every tenant database.
#
#  Reads connection settings from .env beside this script (see .env.example),
#  or from variables already exported in the shell, which win over the file.
#
#  Dry run unless --apply is passed. The dry run writes nothing: it prints, per
#  database, how many permissions would be inserted and a sample document.
#
#  Every run is written to a timestamped transcript in ./logs.
#
#  Usage:
#    ./run.sh                              dry run against everything
#    ./run.sh --only-dbs acme_db           dry run, one tenant
#    ./run.sh --only-dbs acme_db --apply   seed one tenant
#    ./run.sh --apply --yes                full rollout, no prompt
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SEED_SCRIPT="$SCRIPT_DIR/seed-permissions.js"
ENV_FILE="$SCRIPT_DIR/.env"
LOG_DIR="$SCRIPT_DIR/logs"
STAMP="$(date -u +%Y%m%d_%H%M%S)"
LOG_FILE="$LOG_DIR/seed_$STAMP.log"

APPLY=false
ASSUME_YES=false
ONLY_DBS_ARG=""

while [ $# -gt 0 ]; do
    case "$1" in
        --apply)     APPLY=true; shift ;;
        --yes|-y)    ASSUME_YES=true; shift ;;
        --only-dbs)  ONLY_DBS_ARG="${2:-}"; shift 2 ;;
        -h|--help)   sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *)           echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done

[ -f "$SEED_SCRIPT" ] || { echo "seed-permissions.js not found beside this script." >&2; exit 1; }

# --- settings: .env first, then anything already in the environment -----------

if [ -f "$ENV_FILE" ]; then
    echo "  Settings file   : $ENV_FILE"
    # Values already exported win, so CI secrets override the file.
    while IFS= read -r line || [ -n "$line" ]; do
        case "$line" in ''|\#*) continue ;; esac
        key="${line%%=*}"
        val="${line#*=}"
        key="$(printf '%s' "$key" | tr -d '[:space:]')"
        val="${val#"${val%%[![:space:]]*}"}"
        val="${val%"${val##*[![:space:]]}"}"
        case "$val" in
            \"*\") val="${val%\"}"; val="${val#\"}" ;;
            \'*\') val="${val%\'}"; val="${val#\'}" ;;
        esac
        [ -z "$key" ] && continue
        if [ -z "${!key:-}" ]; then export "$key=$val"; fi
    done < "$ENV_FILE"
else
    echo "  Settings file   : (none - using environment variables)"
fi

MONGO_URI="${BLOCKS_MONGO_URI:-}"
ONLY_DBS_RAW="${ONLY_DBS_ARG:-${SEED_ONLY_DBS:-}}"
ORG_ID="${SEED_ORGANIZATION_ID:-default}"
CREATED_BY="${SEED_CREATED_BY:-}"
NAME_TPL="${SEED_NAME_TEMPLATE:-{db} - {name\}}"
ROLES_RAW="${SEED_ROLES:-}"
DOCKER_IMAGE="${SEED_DOCKER_IMAGE:-mongo:7}"

if [ -z "$MONGO_URI" ]; then
    cat >&2 <<EOF

  ERROR: BLOCKS_MONGO_URI is not set.

  Copy .env.example to .env and fill in the connection string:

      cp "$SCRIPT_DIR/.env.example" "$ENV_FILE"

  Or export it for this session only:

      export BLOCKS_MONGO_URI='mongodb://user:pass@host:27017/?authSource=admin'

EOF
    exit 1
fi

# Comma-separated list -> JSON array.
json_array() {
    local raw="$1" out="" item
    [ -z "$raw" ] && { printf '[]'; return; }
    local IFS=','
    for item in $raw; do
        item="${item#"${item%%[![:space:]]*}"}"
        item="${item%"${item##*[![:space:]]}"}"
        [ -z "$item" ] && continue
        [ -n "$out" ] && out="$out,"
        out="$out\"$item\""
    done
    printf '[%s]' "$out"
}

# --- the settings handed to the seeder, as one --eval ------------------------

EVAL_SCRIPT=""
[ "$APPLY" = true ] && EVAL_SCRIPT="SEED_APPLY=true; "
EVAL_SCRIPT="${EVAL_SCRIPT}SEED_ONLY_DBS=$(json_array "$ONLY_DBS_RAW"); "
EVAL_SCRIPT="${EVAL_SCRIPT}SEED_ROLES=$(json_array "$ROLES_RAW"); "
EVAL_SCRIPT="${EVAL_SCRIPT}SEED_ORGANIZATION_ID=\"$ORG_ID\"; "
EVAL_SCRIPT="${EVAL_SCRIPT}SEED_NAME_TEMPLATE=\"$NAME_TPL\""
[ -n "$CREATED_BY" ] && EVAL_SCRIPT="${EVAL_SCRIPT}; SEED_CREATED_BY=\"$CREATED_BY\""

# --- how we will invoke mongosh ----------------------------------------------

USE_DOCKER=false
if command -v mongosh >/dev/null 2>&1; then
    RUNNER="$(command -v mongosh)"
else
    if ! command -v docker >/dev/null 2>&1; then
        cat >&2 <<EOF

  ERROR: neither mongosh nor docker is available.
  Install MongoDB Shell (https://www.mongodb.com/try/download/shell)
  or Docker, which can run it from the $DOCKER_IMAGE image.

EOF
        exit 1
    fi
    USE_DOCKER=true
    RUNNER="docker ($DOCKER_IMAGE)"
fi

if [ -z "$ONLY_DBS_RAW" ]; then TARGETS="every tenant"; else TARGETS="$ONLY_DBS_RAW"; fi

echo ""
echo "=========================================="
echo "  blocks-utilities Permission Seeder"
echo "=========================================="
if [ "$APPLY" = true ]; then
    echo "  Mode            : APPLY - inserts will be committed"
else
    echo "  Mode            : DRY RUN - nothing will be written"
fi
echo "  mongosh via     : $RUNNER"
echo "  Target tenants  : $TARGETS"
echo "  OrganizationId  : $ORG_ID"
echo "  Transcript      : $LOG_FILE"
echo "=========================================="
echo ""

# --- confirm before writing to production ------------------------------------

if [ "$APPLY" = true ] && [ "$ASSUME_YES" != true ]; then
    echo "  About to insert permissions into $TARGETS."
    echo "  Each Permissions collection is backed up first. Existing rows are never modified."
    echo ""
    printf '  Type YES to continue: '
    read -r answer
    if [ "$answer" != "YES" ]; then
        echo "  Aborted."
        exit 1
    fi
    echo ""
fi

mkdir -p "$LOG_DIR"

# --- run ----------------------------------------------------------------------

set +e
if [ "$USE_DOCKER" = true ]; then
    OUTPUT="$(docker run --rm -v "$SCRIPT_DIR:/scripts" "$DOCKER_IMAGE" \
        mongosh "$MONGO_URI" --quiet --eval "$EVAL_SCRIPT" --file /scripts/seed-permissions.js 2>&1)"
else
    OUTPUT="$(mongosh "$MONGO_URI" --quiet --eval "$EVAL_SCRIPT" --file "$SEED_SCRIPT" 2>&1)"
fi
EXIT_CODE=$?
set -e

printf '%s\n' "$OUTPUT"

# The URI carries a password; keep it out of the transcript.
{
    echo "blocks-utilities permission seeder"
    echo "UTC          : $STAMP"
    echo "Mode         : $([ "$APPLY" = true ] && echo APPLY || echo 'DRY RUN')"
    echo "Targets      : $TARGETS"
    echo "Runner       : $RUNNER"
    echo "Exit code    : $EXIT_CODE"
    echo ""
    printf '%s\n' "$OUTPUT"
} > "$LOG_FILE"

echo ""
echo "  Transcript written to $LOG_FILE"

if [ "$EXIT_CODE" -ne 0 ]; then
    echo "  mongosh exited with code $EXIT_CODE" >&2
    exit "$EXIT_CODE"
fi

if [ "$APPLY" != true ]; then
    echo "  Dry run only. Re-run with --apply to commit."
fi

echo ""
