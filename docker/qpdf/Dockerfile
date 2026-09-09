# Local-dev image for PdfToolingOptions.QpdfExecutionMode=Docker only. Every deployed environment
# runs Direct (qpdf installed straight into Dockerfile.worker) - this lets a developer run the
# Worker without installing qpdf locally.
#   docker build -t blocks-qpdf:dev ./docker/qpdf
FROM debian:bookworm-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends qpdf \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /work
