# Unsigned intermediate plugin candidates only; no LGTM runtime trust/adoption.
FROM grafana/otel-lgtm:0.38.1@sha256:0df9bc14142000b161344bd24caf6ddd319b7319055aedaf9b0692b125730af7 AS vendor
FROM golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 AS builder
ENV CGO_ENABLED=0 GOTOOLCHAIN=local
WORKDIR /tools
COPY go.mod verify.go ./
COPY cmd/rebuild-plugins ./cmd/rebuild-plugins
COPY source-inventory.json /reviewed/source-inventory.json
COPY --from=vendor /otel-lgtm/grafana/data/plugins-bundled /vendor-plugins
ARG PLUGIN_ID=
RUN --mount=type=cache,id=tunner-lgtm-go1273-mod,target=/go/pkg/mod,sharing=locked \
    --mount=type=cache,id=tunner-lgtm-go1273-build,target=/root/.cache/go-build,sharing=locked \
    go run ./cmd/rebuild-plugins -inventory /reviewed/source-inventory.json -vendor-root /vendor-plugins -output /out -plugin "$PLUGIN_ID"
FROM scratch AS candidate
COPY --from=builder /out/ /
LABEL io.tunner.scope="unsigned-dev-plugin-candidate-not-runtime-accepted"
