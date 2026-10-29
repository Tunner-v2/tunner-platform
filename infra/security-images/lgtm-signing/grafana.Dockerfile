# Unsigned intermediate plugin candidates only; no LGTM runtime trust/adoption.
FROM grafana/otel-lgtm:0.38.1@sha256:0df9bc14142000b161344bd24caf6ddd319b7319055aedaf9b0692b125730af7 AS vendor
FROM golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 AS builder
ENV CGO_ENABLED=0 GOTOOLCHAIN=local GOMAXPROCS=2
WORKDIR /tools
COPY go.mod verify.go ./
COPY cmd/rebuild-plugins ./cmd/rebuild-plugins
COPY source-inventory.json /reviewed/source-inventory.json
COPY --from=vendor /otel-lgtm/grafana/data/plugins-bundled /vendor-plugins
ARG PLUGIN_ID=
RUN --mount=type=cache,id=tunner-lgtm-go1273-mod,target=/go/pkg/mod,sharing=shared \
    --mount=type=cache,id=tunner-lgtm-go1273-build,target=/root/.cache/go-build,sharing=shared \
    go run ./cmd/rebuild-plugins -inventory /reviewed/source-inventory.json -vendor-root /vendor-plugins -output /out -plugin "$PLUGIN_ID"
FROM scratch AS unsigned-plugins
COPY --from=builder /out/ /
LABEL io.tunner.scope="unsigned-dev-plugin-candidate-not-runtime-accepted"

FROM golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 AS grafana-builder
ENV CGO_ENABLED=0 GOTOOLCHAIN=local GOMAXPROCS=2
WORKDIR /tools
COPY go.mod verify.go ./
COPY cmd/sign-dev ./cmd/sign-dev
COPY source-inventory.json /reviewed/source-inventory.json
COPY --from=unsigned-plugins /plugins /out/plugins
COPY --from=unsigned-plugins /provenance /out/provenance/plugins
ADD --checksum=sha256:fdd57503b5f591e951ea3dff80ab53a41487c3373dc89370da2e36e7bf752f52 https://codeload.github.com/grafana/grafana/tar.gz/refs/tags/v13.2.4 /tmp/grafana.tar.gz
WORKDIR /src/grafana
RUN tar -xzf /tmp/grafana.tar.gz --strip-components=1 && mkdir -p /out/bin /out/provenance/grafana
COPY grafana-signature.patch /tmp/tunner-signature.patch
RUN git apply --check /tmp/tunner-signature.patch && git apply /tmp/tunner-signature.patch
COPY verify.go pkg/plugins/manager/signature/tunnerdev/verify.go
RUN sed -i 's/^package signing$/package tunnerdev/' pkg/plugins/manager/signature/tunnerdev/verify.go
COPY grafana-adapter.go.txt pkg/plugins/manager/signature/tunner_dev.go
COPY grafana-adapter-test.go.txt pkg/plugins/manager/signature/tunner_dev_test.go
RUN cd /tools && go run ./cmd/sign-dev -plugins /out/plugins -inventory /reviewed/source-inventory.json -policy-output /src/grafana/pkg/plugins/manager/signature/tunner_dev_policy.go -audit-output /out/provenance/grafana/signing-public.json
RUN --mount=type=cache,id=tunner-lgtm-go1273-mod,target=/go/pkg/mod,sharing=shared \
    --mount=type=cache,id=tunner-lgtm-go1273-build,target=/root/.cache/go-build,sharing=shared \
    set -eu; go get github.com/go-jose/go-jose/v4@v4.1.6 github.com/getkin/kin-openapi@v0.149.0; \
    go test -p=2 -count=1 -json ./pkg/plugins/manager/signature/... > /out/provenance/grafana/native-signature-tests.json; \
    go build -p=2 -trimpath -ldflags='-s -w -X main.version=13.2.4 -X main.commit=af4a33e3786594ad72f72d97e6744010330c2977 -X main.buildBranch=v13.2.4-tunner-dev -X main.buildstamp=1793145600' -o /out/bin/grafana ./pkg/cmd/grafana; \
    go version -m /out/bin/grafana > /out/provenance/grafana/buildinfo.txt; \
    cp go.mod go.sum go.work go.work.sum /out/provenance/grafana/; \
    go list -m -json all > /out/provenance/grafana/modules.json
FROM scratch AS candidate
COPY --from=grafana-builder /out/ /
LABEL io.tunner.scope="signed-dev-grafana-candidate-not-runtime-accepted"
