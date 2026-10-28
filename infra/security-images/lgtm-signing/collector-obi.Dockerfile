# Private candidates preserve the complete generated BPF/Java source and collector manifest.
FROM golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 AS obi-source
ENV CGO_ENABLED=0 GOTOOLCHAIN=local GOMAXPROCS=2
ADD --checksum=sha256:d3cbec602e3ab7b357d2f4a57b29e502365f9098808418e8b4d96dec8fa9281f https://github.com/open-telemetry/opentelemetry-ebpf-instrumentation/releases/download/v0.15.2/obi-v0.15.2-source-generated.tar.gz /tmp/obi.tar.gz
WORKDIR /src/obi
RUN tar -xzf /tmp/obi.tar.gz --strip-components=1 && test -s pkg/internal/java/embedded/obi-java-agent.jar && test -s pkg/ebpf/common/bpf_x86_bpfel.o
FROM obi-source AS obi-builder
RUN --mount=type=cache,id=tunner-lgtm-go1273-mod,target=/go/pkg/mod,sharing=locked \
    --mount=type=cache,id=tunner-lgtm-go1273-build,target=/root/.cache/go-build,sharing=locked \
    set -eu; mkdir -p /out/bin /out/provenance/obi; \
    go mod verify; \
    go test -p=2 -count=1 -json ./cmd/obi/... ./pkg/buildinfo > /out/provenance/obi/upstream-tests.json; \
    go build -p=2 -trimpath -ldflags='-s -w -X go.opentelemetry.io/obi/pkg/buildinfo.Version=v0.15.2 -X go.opentelemetry.io/obi/pkg/buildinfo.Revision=b28e4e7' -o /out/bin/obi ./cmd/obi/main.go; \
    go version -m /out/bin/obi > /out/provenance/obi/buildinfo.txt; \
    cp go.mod go.sum LICENSE NOTICE /out/provenance/obi/; \
    find pkg -type f '(' -name '*_bpfel.o' -o -name '*_bpfeb.o' -o -name '*.jar' ')' -exec sha256sum '{}' ';' > /out/provenance/obi/embedded-assets.sha256; \
    go list -m -json all > /out/provenance/obi/modules.json
FROM obi-source AS collector-builder
ADD --checksum=sha256:589a91e904cf78ea88c543c0ef2170529b89b74904386bfa87876ff07d60748e https://codeload.github.com/open-telemetry/opentelemetry-collector-releases/tar.gz/6c65793ec707f673304e52dfcb642684fda1a048 /tmp/collector.tar.gz
WORKDIR /src/collector
RUN tar -xzf /tmp/collector.tar.gz --strip-components=1 && mkdir -p internal/obi-src /out/bin /out/provenance/collector && cp -a /src/obi/. internal/obi-src/
RUN --mount=type=cache,id=tunner-lgtm-go1273-mod,target=/go/pkg/mod,sharing=locked \
    --mount=type=cache,id=tunner-lgtm-go1273-build,target=/root/.cache/go-build,sharing=locked \
    set -eu; go install go.opentelemetry.io/collector/cmd/builder@v0.163.0; \
    cd distributions/otelcol-contrib; \
    /go/bin/builder --skip-compilation --config manifest.yaml; \
    cp manifest.yaml /out/provenance/collector/manifest.yaml; \
    cd _build; \
    go get github.com/go-jose/go-jose/v4@v4.1.6; go mod tidy; go mod verify; \
    go build -p=2 -trimpath -tags grpcnotrace -ldflags='-s -w' -o /out/bin/otelcol-contrib .; \
    cp go.mod go.sum components.go /out/provenance/collector/; \    go version -m /out/bin/otelcol-contrib > /out/provenance/collector/buildinfo.txt; \
    go list -m -json all > /out/provenance/collector/modules.json; \
    /out/bin/otelcol-contrib --version > /out/provenance/collector/version.txt; \
    /out/bin/otelcol-contrib components > /out/provenance/collector/components.txt
FROM scratch AS obi-candidate
COPY --from=obi-builder /out/ /
LABEL io.tunner.scope="unsigned-dev-service-candidate-not-runtime-accepted"
FROM scratch AS collector-candidate
COPY --from=collector-builder /out/ /
LABEL io.tunner.scope="unsigned-dev-service-candidate-not-runtime-accepted"
