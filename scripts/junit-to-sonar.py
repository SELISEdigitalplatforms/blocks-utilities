#!/usr/bin/env python3
"""Convert a JUnit XML report into SonarQube's Generic Test Execution format.

SonarQube reads .NET test results straight from VSTest .trx files, but for
JS/TS it only accepts its own generic format - a plain JUnit report from vitest
is ignored. This bridges that gap so client test counts show up next to the
client coverage.

Usage:
    junit-to-sonar.py <junit.xml> <output.xml> [path-prefix]

`path-prefix` is prepended to each test file path so it resolves from the
SonarQube project base directory (e.g. "client" when vitest ran inside client/).
Test files that no longer exist on disk are dropped - Sonar rejects the whole
report if any path is unknown.
"""

import os
import sys
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape, quoteattr


def duration_ms(node):
    try:
        return max(0, int(round(float(node.get("time") or 0) * 1000)))
    except (TypeError, ValueError):
        return 0


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)

    junit_path, out_path = sys.argv[1], sys.argv[2]
    prefix = sys.argv[3].strip("/") if len(sys.argv) > 3 else ""

    root = ET.parse(junit_path).getroot()
    suites = root.iter("testsuite") if root.tag == "testsuites" else [root]

    files = {}
    for suite in suites:
        for case in suite.findall("testcase"):
            rel = case.get("classname") or case.get("file") or suite.get("name") or ""
            rel = rel.strip()
            if not rel:
                continue
            path = f"{prefix}/{rel}" if prefix else rel
            files.setdefault(path, []).append(case)

    kept = dropped = total = 0
    lines = ['<?xml version="1.0" encoding="UTF-8"?>', '<testExecutions version="1">']

    for path in sorted(files):
        if not os.path.isfile(path):
            dropped += 1
            continue
        kept += 1
        lines.append(f"  <file path={quoteattr(path)}>")
        for case in files[path]:
            total += 1
            name = case.get("name") or "unnamed"
            attrs = f"name={quoteattr(name)} duration=\"{duration_ms(case)}\""
            failure = case.find("failure")
            error = case.find("error")
            skipped = case.find("skipped")
            if error is not None:
                msg = error.get("message") or "error"
                lines.append(f"    <testCase {attrs}>")
                lines.append(f"      <error message={quoteattr(msg)}>{escape((error.text or '').strip())}</error>")
                lines.append("    </testCase>")
            elif failure is not None:
                msg = failure.get("message") or "failed"
                lines.append(f"    <testCase {attrs}>")
                lines.append(f"      <failure message={quoteattr(msg)}>{escape((failure.text or '').strip())}</failure>")
                lines.append("    </testCase>")
            elif skipped is not None:
                lines.append(f"    <testCase {attrs}>")
                lines.append(f"      <skipped message={quoteattr(skipped.get('message') or 'skipped')}/>")
                lines.append("    </testCase>")
            else:
                lines.append(f"    <testCase {attrs}/>")
        lines.append("  </file>")

    lines.append("</testExecutions>")

    with open(out_path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")

    print(f"junit-to-sonar: {total} test cases across {kept} files -> {out_path}"
          + (f" ({dropped} unresolved file path(s) dropped)" if dropped else ""))


if __name__ == "__main__":
    main()
