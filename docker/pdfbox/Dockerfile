# Build context is the REPOSITORY ROOT, not this directory, so the Java sources stay in one place
# (tools/pdfbox) instead of being duplicated here:
#   docker build -t blocks-pdfbox:dev -f docker/pdfbox/Dockerfile .
#
# Local-dev image for PdfToolingOptions.PdfBoxExecutionMode=Docker only. Every deployed environment
# runs Direct (PDFBox compiled straight into Dockerfile.worker) - this lets a developer run the
# Worker without installing a JDK locally.
FROM eclipse-temurin:17-jdk AS build
ARG PDFBOX_VERSION=3.0.8

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*

COPY tools/pdfbox/*.java /tmp/pdfbox-src/
RUN mkdir -p /opt/pdfbox \
    && curl -fsSL "https://repo1.maven.org/maven2/org/apache/pdfbox/pdfbox-app/${PDFBOX_VERSION}/pdfbox-app-${PDFBOX_VERSION}.jar" -o /opt/pdfbox/pdfbox-app.jar \
    && javac -cp /opt/pdfbox/pdfbox-app.jar -d /opt/pdfbox /tmp/pdfbox-src/*.java

FROM eclipse-temurin:17-jre
COPY --from=build /opt/pdfbox /opt/pdfbox
WORKDIR /work
# The main class is passed as the first container argument so one image serves every PDFBox tool.
ENTRYPOINT ["java", "-cp", "/opt/pdfbox/pdfbox-app.jar:/opt/pdfbox"]
