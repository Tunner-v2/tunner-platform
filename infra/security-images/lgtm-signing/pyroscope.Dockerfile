# Full Pyroscope production assets are rebuilt and embedded, never replaced by a dev stub.
FROM node:24.22.1-bookworm@sha256:5630d96570d4ca4a8523fda3d169d0114554d0f48293e1ae3db6be7c79b42a17 AS ui-builder
ADD --checksum=sha256:680d14061434afb10738cc20139e111668e09acd45a9ff5c2df3e039bc6fb88c https://codeload.github.com/grafana/pyroscope/tar.gz/36d558a4799e86ce9814a5348e6ecc300e4ed192 /tmp/pyroscope.tar.gz
WORKDIR /src/pyroscope
RUN tar -xzf /tmp/pyroscope.tar.gz --strip-components=1 && mkdir -p /out/provenance/pyroscope
WORKDIR /src/pyroscope/ui
RUN corepack enable && yarn install --immutable
ARG UI_AUDIT_EPOCH=required
RUN test "$UI_AUDIT_EPOCH" != required && yarn npm audit --environment production --severity high --json > /out/provenance/pyroscope/ui-audit.json
RUN yarn test && yarn build && test -s dist/index.html
FROM golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 AS builder
ENV CGO_ENABLED=0 GOTOOLCHAIN=local GOMAXPROCS=2 GOAMD64=v2
COPY --from=ui-builder /src/pyroscope /src/pyroscope
COPY --from=ui-builder /out /out
WORKDIR /src/pyroscope
RUN --mount=type=cache,id=tunner-lgtm-go1273-mod,target=/go/pkg/mod,sharing=shared \
    --mount=type=cache,id=tunner-lgtm-go1273-build,target=/root/.cache/go-build,sharing=shared \
    set -eu; mkdir -p /out/bin; go get github.com/go-jose/go-jose/v4@v4.1.6; go mod verify; \
    go test -p=2 -tags embedassets -count=1 -json ./ui/... ./pkg/util/build/... > /out/provenance/pyroscope/upstream-tests.json; \
    go build -p=2 -trimpath -tags netgo,embedassets -ldflags='-s -w -X github.com/grafana/pyroscope/v2/pkg/util/build.Version=2.4.1 -X github.com/grafana/pyroscope/v2/pkg/util/build.Revision=36d558a4799e86ce9814a5348e6ecc300e4ed192 -X github.com/grafana/pyroscope/v2/pkg/util/build.Branch=v2.4.1-tunner-dev' -o /out/bin/pyroscope ./cmd/pyroscope; \
    go version -m /out/bin/pyroscope > /out/provenance/pyroscope/buildinfo.txt; \
    cp go.mod go.sum LICENSE ui/package.json ui/yarn.lock /out/provenance/pyroscope/; \
    go list -m -json all > /out/provenance/pyroscope/modules.json
FROM scratch AS candidate
COPY --from=builder /out/ /
LABEL io.tunner.scope="unsigned-dev-service-candidate-not-runtime-accepted"
