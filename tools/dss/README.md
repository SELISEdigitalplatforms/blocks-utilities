# DSS signature validator

The Java side of PDF signature validation ([spec](../../docs/pdf-signature-validation/README.md)):
a long-running process that validates PDF signatures with EU DSS 6.5. `Dockerfile.worker`'s
`dss-build` stage resolves DSS from `pom.xml` and compiles `DssValidator.java` into `/opt/dss`.

## Running it

```sh
java -cp "/opt/dss/lib/*:/opt/dss" DssValidator \
  --cache-dir /tmp/dss-tl-cache \
  --lotl-signers /opt/dss/eu-lotl-signers.pem \
  [--anchors extra-anchors.pem] [--refresh-hours 24] [--once file.pdf]
```

- The trusted lists load from `--cache-dir` first, then refresh from the network in the background.
  With an empty cache the first download takes about 2 minutes; a load from the cache takes about
  40–50 s.
- `--once file.pdf` waits for the lists if needed, prints one verdict and exits. Use it for manual
  checks and support.
- DSS logs to stderr. Add `-Dorg.slf4j.simpleLogger.defaultLogLevel=warn` to quieten it.

## Protocol

One JSON object per line on stdin; one JSON object per line on stdout, carrying the same `id`.

| Request | Reply |
|---|---|
| `{"op":"status","id":"1"}` | `{"id":"1","ok":true,"ready":true,"trustedListsLoadedAt":"…","trustedCertificateCount":4790}` |
| `{"op":"validate","id":"2","path":"/tmp/x.pdf"}` | `{"id":"2","ok":true,"verdict":{…}}`, verdict fields as in the spec's Domain Model |
| Anything that fails | `{"id":"…","ok":false,"errorCode":"…","errorMessage":"…"}` |

Error codes: `trust_lists_unavailable` (not loaded yet; re-queue the job, it is not a verdict),
`input_file_not_found`, `input_not_pdf`, `input_password_protected`, `validator_error`,
`invalid_request`. A timeout, or a JVM that dies, is detected by the .NET side, not reported here.

Revocation data is never fetched online. A signature reaches LT only on the data the document
carries.

## `eu-lotl-signers.pem`

The certificates the European Commission announces in the Official Journal as the signers of the
EU List of Trusted Lists. They come from DSS 6.5's demonstration keystore
(`esig/dss-demonstrations`, `dss-demo-webapp/src/main/resources/keystore.p12`) and are public
certificates only. Replace the file, and `--oj-url` if it changed, when the Official Journal
announces new signers. The earliest current certificate expires in April 2027.

## Load check

`load-check.sh` is the repeatable check for AC-18 (100 files queued at once on one worker have a
result within 5 minutes). It runs the validator's `--bench` mode in a worker image: after the trusted
lists have loaded it validates one file N times in a row, one at a time, which is how a worker serves
its queue, and prints the per-file timings as one JSON line followed by a pass or fail against the
5-minute budget.

```sh
tools/dss/load-check.sh <worker-image> <signed.pdf> [count] [trusted-list-cache-dir]

# what a pod with two CPUs would do
LOAD_CHECK_DOCKER_ARGS="--cpus=2" tools/dss/load-check.sh <worker-image> <signed.pdf> 100 ./tl-cache
```

- Use a real signed file like production's. It is read, never modified or kept, and none is in the
  repository because it may hold customer data.
- Give it a directory of already-downloaded trusted lists (the `--cache-dir` of an earlier run) so it
  starts from the warm cache a restarted worker has, not a two-minute download.
- **Run it on a machine with nothing else busy.** Validation time depends on the CPU the process
  gets, and a run beside a browser and a language server can be several times slower than the same
  run on a quiet machine. Compare numbers only between runs made in the same conditions.
- It measures the validator process only. Queueing, the storage download and the notification are
  small next to a validation that takes seconds, and the same file every time is a little kinder
  than a burst of different ones.
