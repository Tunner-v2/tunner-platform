# Rebuild both complete upstream UIs and both binaries from exact Prometheus3.15.1 source.
FROM node:24.22.1-bookworm@sha256:5630d96570d4ca4a8523fda3d169d0114554d0f48293e1ae3db6be7c79b42a17 AS ui-builder
ADD --checksum=sha256:4636cc523bda30bdf92bf7b05b4491bb3d3038d89ebb3223c7b9a71f23e9e9c9 https://codeload.github.com/prometheus/prometheus/tar.gz/bae3eced0a5369e050e83ea73bc381d7c4b1a719 /tmp/prometheus.tar.gz
WORKDIR /src/prometheus
RUN tar -xzf /tmp/prometheus.tar.gz --strip-components=1 && mkdir -p /out/provenance/prometheus/ui
COPY prometheus-ui-security.patch /tmp/ui-security.patch
RUN git apply --check /tmp/ui-security.patch && git apply /tmp/ui-security.patch
WORKDIR /src/prometheus/web/ui
RUN corepack enable && corepack prepare pnpm@11.19.0 --activate && pnpm install --frozen-lockfile && cd react-app && pnpm install --frozen-lockfile
RUN set -eu; CI='' pnpm run build > /out/provenance/prometheus/ui/build.log 2>&1 || { cat /out/provenance/prometheus/ui/build.log; exit 1; }; \
    test -s static/mantine-ui/index.html; test -s static/react-app/index.html; \
    CI=true pnpm test > /out/provenance/prometheus/ui/workspace-tests.log 2>&1 || { cat /out/provenance/prometheus/ui/workspace-tests.log; exit 1; }; \
    cd react-app; CI=true pnpm test > /out/provenance/prometheus/ui/legacy-tests.log 2>&1 || { cat /out/provenance/prometheus/ui/legacy-tests.log; exit 1; }
RUN set -eu; mkdir -p /audit-input/web/ui; \
    cp package.json pnpm-lock.yaml pnpm-workspace.yaml /audit-input/web/ui/; \
    for part in mantine-ui module/codemirror-promql module/lezer-promql react-app; do mkdir -p /audit-input/web/ui/$part; cp $part/package.json /audit-input/web/ui/$part/; done; \
    cp react-app/pnpm-lock.yaml react-app/pnpm-workspace.yaml /audit-input/web/ui/react-app/; \
    mkdir -p /audit-input/web/ui/mantine-ui/src/promql/tools; cp mantine-ui/src/promql/tools/go.mod mantine-ui/src/promql/tools/go.sum /audit-input/web/ui/mantine-ui/src/promql/tools/; \
    cp -a /audit-input/web/ui/. /out/provenance/prometheus/ui/manifests; \
    find static -type f -exec sha256sum '{}' ';' > /out/provenance/prometheus/ui/assets.sha256
FROM aquasec/trivy:0.70.0@sha256:be1190afcb28352bfddc4ddeb71470835d16462af68d310f9f4bca710961a41e AS ui-audit
COPY --from=ui-builder /audit-input /audit-input
ARG UI_AUDIT_EPOCH=required
RUN --mount=type=cache,id=tunner-security-trivy-db,target=/root/.cache/trivy,sharing=locked \
    test "$UI_AUDIT_EPOCH" != required && mkdir -p /out/provenance/prometheus/ui && trivy fs --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 --format json --list-all-pkgs --output /out/provenance/prometheus/ui/production-audit.json /audit-input
FROM scratch AS ui-proof
COPY --from=ui-builder /out/ /
COPY --from=ui-audit /out/ /
FROM golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 AS builder
ENV CGO_ENABLED=0 GOTOOLCHAIN=local GOMAXPROCS=2
ADD --checksum=sha256:4636cc523bda30bdf92bf7b05b4491bb3d3038d89ebb3223c7b9a71f23e9e9c9 https://codeload.github.com/prometheus/prometheus/tar.gz/bae3eced0a5369e050e83ea73bc381d7c4b1a719 /tmp/prometheus.tar.gz
WORKDIR /src/prometheus
RUN tar -xzf /tmp/prometheus.tar.gz --strip-components=1 && mkdir -p /out/bin /out/provenance/prometheus
COPY prometheus-ui-security.patch /tmp/ui-security.patch
RUN git apply --check /tmp/ui-security.patch && git apply /tmp/ui-security.patch
COPY --from=ui-builder /src/prometheus/web/ui/static /src/prometheus/web/ui/static
COPY --from=ui-builder /out /out
COPY --from=ui-audit /out /out
COPY prometheus-assets_test.go.txt web/ui/tunner_assets_test.go
RUN bash scripts/compress_assets.sh
RUN --mount=type=cache,id=tunner-lgtm-go1273-mod,target=/go/pkg/mod,sharing=shared \
    --mount=type=cache,id=tunner-lgtm-go1273-build,target=/root/.cache/go-build,sharing=shared \
    set -eu; go get golang.org/x/net@v0.62.0; go mod verify; \
    go test -mod=readonly -p=2 -tags builtinassets -count=1 -json ./web/ui/... > /out/provenance/prometheus/upstream-tests.json || { cat /out/provenance/prometheus/upstream-tests.json; exit 1; }; \
    for name in prometheus promtool; do \
      go build -mod=readonly -p=2 -trimpath -tags netgo,builtinassets -ldflags='-s -w -X github.com/prometheus/common/version.Version=3.15.1 -X github.com/prometheus/common/version.Revision=bae3eced0a5369e050e83ea73bc381d7c4b1a719 -X github.com/prometheus/common/version.Branch=v3.15.1-tunner-dev' -o /out/bin/$name ./cmd/$name; \
      /out/bin/$name --version > /out/provenance/prometheus/$name-version.txt 2>&1; \
      go version -m /out/bin/$name > /out/provenance/prometheus/$name-buildinfo.txt; \
    done; \
    cp go.mod go.sum LICENSE NOTICE /out/provenance/prometheus/; go list -mod=readonly -m -json all > /out/provenance/prometheus/modules.json
FROM scratch AS candidate
COPY --from=builder /out/ /
LABEL io.tunner.scope="unsigned-dev-service-candidate-not-runtime-accepted" io.tunner.prometheus.source="bae3eced0a5369e050e83ea73bc381d7c4b1a719"
