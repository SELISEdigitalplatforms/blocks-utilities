#!/usr/bin/env python3
"""Reshape TruffleHog JSONL for the Shield explorer.

TruffleHog's `git file://...` output carries Git source metadata; the Shield
explorer expects GitHub metadata with a browsable blob link. This rewrites each
record accordingly and passes everything else through untouched.

Usage:
    th-transform.py <service>        # e.g. "os" -> blocks-os  (stdin -> stdout)
    th-transform.py --count-verified # print the number of verified findings

Nothing is printed except the transformed records (or the count) - finding
details must never leak into a CI log.
"""

import json
import sys

ORG = "https://github.com/SELISEdigitalplatforms"


def records(stream):
    for line in stream:
        line = line.strip()
        if not line.startswith("{"):
            continue
        try:
            yield json.loads(line)
        except ValueError:
            continue


def count_verified(stream):
    return sum(1 for d in records(stream) if d.get("Verified"))


def transform(stream, service, out):
    base = f"{ORG}/blocks-{service}"
    for d in records(stream):
        git = d.get("SourceMetadata", {}).get("Data", {}).get("Git")
        if git:
            commit = git.get("commit", "")
            path = git.get("file", "")
            line_no = git.get("line", 0)
            link = f"{base}/blob/{commit}/{path}#L{line_no}" if commit and path else base
            d["SourceMetadata"]["Data"] = {
                "Github": {
                    "repository": f"{base}.git",
                    "commit": commit,
                    "email": git.get("email", ""),
                    "file": path,
                    "timestamp": git.get("timestamp", ""),
                    "line": line_no,
                    "link": link,
                }
            }
        out.write(json.dumps(d) + "\n")


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    if sys.argv[1] == "--count-verified":
        print(count_verified(sys.stdin))
        return
    transform(sys.stdin, sys.argv[1], sys.stdout)


if __name__ == "__main__":
    main()
