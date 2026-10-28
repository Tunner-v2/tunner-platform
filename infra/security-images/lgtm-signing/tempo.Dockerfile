# Unsigned, non-runtime Tempo service candidates. Full LGTM acceptance remains separate.
FROM golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 AS tempo-builder
ENV CGO_ENABLED=0 GOTOOLCHAIN=local GOMAXPROCS=2
ADD --checksum=sha256:a42f9ffacb3f8fdca315f15fd9fda855607c43cfb9c20e4ec86c084585036497 https://codeload.github.com/grafana/tempo/tar.gz/9db7df79d2af105a90d96484e6403e1fda0d011e /tmp/tempo.tar.gz
WORKDIR /src/tempo
RUN tar -xzf /tmp/tempo.tar.gz --strip-components=1 && mkdir -p /out/provenance/tempo /out/bin
# Use the checksum-verified updated module graph rather than stale upstream vendor/modules.txt.
RUN --mount=type=cache,id=tunner-lgtm-go1273-mod,target=/go/pkg/mod,sharing=locked \
    --mount=type=cache,id=tunner-lgtm-go1273-build,target=/root/.cache/go-build,sharing=locked \
    set -eu; \
    go get github.com/go-jose/go-jose/v4@v4.1.6 && go mod tidy && go mod verify && \
    go test -mod=readonly -p=2 -count=1 -json ./tempodb/backend/s3 -run '^TestSSEConfigEncryptionKeyRedacted$' > /out/provenance/tempo/redaction-tests.json && \
    go test -mod=readonly -p=2 -count=1 -json ./modules/frontend -run 'Limit|Search' > /out/provenance/tempo/query-limit-tests.json && \
    for name in tempo tempo-cli tempo-query; do \
      go build -mod=readonly -p=2 -trimpath -ldflags='-s -w -X main.Version=3.1.1 -X main.Revision=9db7df79d2af105a90d96484e6403e1fda0d011e -X main.Branch=v3.1.1-tunner-dev' -o /out/bin/$name ./cmd/$name || exit 1; \
      go version -m /out/bin/$name > /out/provenance/tempo/$name-buildinfo.txt || exit 1; \
    done && cp go.mod go.sum /out/provenance/tempo/ && go list -mod=readonly -m -json all > /out/provenance/tempo/modules.json
FROM scratch AS candidate
COPY --from=tempo-builder /out/ /
LABEL io.tunner.scope="unsigned-dev-service-candidate-not-runtime-accepted" io.tunner.tempo.source="9db7df79d2af105a90d96484e6403e1fda0d011e" io.tunner.tempo.version="3.1.1"
