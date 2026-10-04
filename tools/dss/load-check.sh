#!/usr/bin/env bash
# Times a burst of signature validations on one validator, the way a worker serves its queue: one
# file at a time, in a row. This is the repeatable check for AC-18 in
# docs/pdf-signature-validation/README.md: 100 files queued at once on one worker have to have a
# result within 5 minutes.
#
# Usage: tools/dss/load-check.sh <worker-image> <signed.pdf> [count] [trusted-list-cache-dir]
#
#   worker-image  an image built from Dockerfile.worker (it carries /opt/dss and a JRE)
#   signed.pdf    a real signed file, ideally one like production's; it is read, never modified, and
#                 not kept anywhere (it may hold customer data, which is why none is committed)
#   count         how many times to validate it, default 100
#   cache-dir     a directory of already-downloaded trusted lists, so the run starts from the warm
#                 cache a restarted worker has instead of spending ~2 minutes downloading them
#
# Set LOAD_CHECK_DOCKER_ARGS to pass extra flags to `docker run`, most usefully the CPU a worker pod
# would get, since validation time depends on it: LOAD_CHECK_DOCKER_ARGS="--cpus=2".
#
# What it measures is the validator process only: from the first file to the last, after the
# trusted lists have loaded. Queueing, download from storage and notification are not in it; they
# are small next to a validation that takes seconds. The same file is used every time, so it is a
# little kinder than a burst of different files.
set -euo pipefail

image="${1:?usage: load-check.sh <worker-image> <signed.pdf> [count] [trusted-list-cache-dir]}"
pdf="${2:?usage: load-check.sh <worker-image> <signed.pdf> [count] [trusted-list-cache-dir]}"
count="${3:-100}"
cache="${4:-}"

# The five-minute budget from AC-18.
budget_ms=300000

[ -f "$pdf" ] || { echo "load-check: no such file: $pdf" >&2; exit 2; }

# Git Bash on Windows mangles container paths in arguments unless told not to, and wants Windows
# host paths for volumes; elsewhere `pwd -W` does not exist and plain `pwd` is right.
export MSYS_NO_PATHCONV=1
host_dir() { (cd "$1" && { pwd -W 2>/dev/null || pwd; }) | tr -d '\r'; }

mounts=(-v "$(host_dir "$(dirname "$pdf")")/$(basename "$pdf"):/in/file.pdf:ro")
if [ -n "$cache" ]; then
  mounts+=(-v "$(host_dir "$cache"):/cache-host:ro")
fi

echo "load-check: validating $(basename "$pdf") $count times with $image ${LOAD_CHECK_DOCKER_ARGS:+($LOAD_CHECK_DOCKER_ARGS) }..." >&2

read -r -a extra <<< "${LOAD_CHECK_DOCKER_ARGS:-}"

# ${extra[@]+...} because an empty array is "unbound" under set -u on the bash 3.2 that macOS ships.
result="$(docker run --rm ${extra[@]+"${extra[@]}"} --entrypoint sh "${mounts[@]}" "$image" -c '
  mkdir -p /tmp/dss-tl-cache
  if [ -d /cache-host ]; then cp /cache-host/* /tmp/dss-tl-cache/; fi
  exec java -Xmx512m -Djava.awt.headless=true \
    -Dorg.slf4j.simpleLogger.defaultLogLevel=error \
    -Dorg.slf4j.simpleLogger.log.org.apache.pdfbox.pdmodel.font=error \
    -cp "/opt/dss/lib/*:/opt/dss" DssValidator \
    --cache-dir /tmp/dss-tl-cache --lotl-signers /opt/dss/eu-lotl-signers.pem \
    --bench /in/file.pdf --count '"$count"' 2>/dev/null
' | tail -n 1)"

echo "$result"

field() { printf '%s' "$result" | sed -n "s/.*\"$1\":\([0-9]*\).*/\1/p"; }

total="$(field totalMs)"
if [ -z "$total" ]; then
  echo "load-check: the validator did not report a result (see the line above)" >&2
  exit 1
fi

# The budget is for 100 files; scale it so a smaller trial run still gives a straight answer.
scaled=$(( budget_ms * count / 100 ))

printf '\n%s files in %s s (ready after %s s). Per file: first %s ms, median %s ms, p95 %s ms, max %s ms.\n' \
  "$count" "$(( total / 1000 ))" "$(( $(field readyAfterMs) / 1000 ))" \
  "$(field firstMs)" "$(field medianMs)" "$(field p95Ms)" "$(field maxMs)"

if [ "$total" -le "$scaled" ]; then
  printf 'AC-18: PASS (%s s, budget %s s for %s files)\n' "$(( total / 1000 ))" "$(( scaled / 1000 ))" "$count"
else
  printf 'AC-18: FAIL (%s s, budget %s s for %s files)\n' "$(( total / 1000 ))" "$(( scaled / 1000 ))" "$count"
  exit 3
fi
