# Preserve exact Loki3.7.9 source and features; repair the bounded Go dependency.
FROM golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 AS builder
ENV CGO_ENABLED=0 GOTOOLCHAIN=local GOMAXPROCS=2
ADD --checksum=sha256:f64c14e3aaeab3c14c013974c6a016e5f61fd1758354ca8dcd320dcce8c656aa https://codeload.github.com/grafana/loki/tar.gz/d3b4a2f76bfc5625abee7ceb00b8896fa3c31549 /tmp/loki.tar.gz
WORKDIR /src/loki
RUN tar -xzf /tmp/loki.tar.gz --strip-components=1 && mkdir -p /out/bin /out/provenance/loki
RUN --mount=type=cache,id=tunner-lgtm-go1273-mod,target=/go/pkg/mod,sharing=shared \
    --mount=type=cache,id=tunner-lgtm-go1273-build,target=/root/.cache/go-build,sharing=shared \
    set -eu; GOFLAGS=-mod=mod go get golang.org/x/net@v0.62.0; go mod verify; \
    go test -mod=readonly -p=2 -count=1 -json ./pkg/ui/... ./pkg/util/build/... > /out/provenance/loki/upstream-tests.json || { cat /out/provenance/loki/upstream-tests.json; exit 1; }; \
    go build -mod=readonly -p=2 -trimpath -tags netgo -ldflags='-s -w -X github.com/grafana/loki/v3/pkg/util/build.Version=3.7.9 -X github.com/grafana/loki/v3/pkg/util/build.Revision=d3b4a2f76bfc5625abee7ceb00b8896fa3c31549 -X github.com/grafana/loki/v3/pkg/util/build.Branch=v3.7.9-tunner-dev' -o /out/bin/loki ./cmd/loki; \
    /out/bin/loki -version > /out/provenance/loki/version.txt 2>&1; \
    go version -m /out/bin/loki > /out/provenance/loki/buildinfo.txt; \
    cp go.mod go.sum LICENSE NOTICE /out/provenance/loki/; go list -mod=readonly -m -json all > /out/provenance/loki/modules.json
FROM scratch AS candidate
COPY --from=builder /out/ /
LABEL io.tunner.scope="unsigned-dev-service-candidate-not-runtime-accepted" io.tunner.loki.source="d3b4a2f76bfc5625abee7ceb00b8896fa3c31549"
