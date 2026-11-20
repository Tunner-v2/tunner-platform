# Reuse the released source distribution, including its built embedded UI.
FROM golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 AS builder
ADD --checksum=sha256:f50ac5b150e780728b0f8c5c1533e6542576ff3834f9a89d6c9921e6759f0109 https://github.com/openbao/openbao/releases/download/v2.7.2/openbao-dist-v2.7.2.tar.xz /tmp/source.tar.xz
WORKDIR /src
RUN apt-get update && apt-get install -y --no-install-recommends xz-utils=5.4.1-1+deb12u2 && rm -rf /var/lib/apt/lists/*
RUN tar -xJf /tmp/source.tar.xz --strip-components=1 && test -f internal/http/web_ui/index.html
ENV CGO_ENABLED=0 GOTOOLCHAIN=local
RUN go get golang.org/x/net@v0.62.0 github.com/go-jose/go-jose/v4@v4.1.6 && go mod vendor && go mod verify
ARG TARGETARCH
RUN --mount=type=cache,id=tunner-security-go1273,target=/root/.cache/go-build,sharing=locked GOOS=linux GOARCH=$TARGETARCH go build -p=2 -trimpath -tags=ui \
    -ldflags='-X github.com/openbao/openbao/v2/internal/version.fullVersion=2.7.2 -X github.com/openbao/openbao/v2/internal/version.GitCommit=9e0a0d3c3504a0a00b45e61355afac5e35c78379 -X github.com/openbao/openbao/v2/internal/version.CommitDate=2026-10-26T19:32:27Z -X github.com/openbao/openbao/v2/internal/version.VersionMetadata=tunner.security1' \
    -o /out/bao . && go version -m /out/bao

FROM openbao/openbao:2.7.2@sha256:5046911075e137e154ce49ab051502f4ec7cf367d484ae4a4dce665d9b90561f
COPY --from=builder --chmod=555 /out/bao /usr/bin/bao
LABEL org.opencontainers.image.title="Tunner security rebuild of OpenBao 2.7.2" \
      org.opencontainers.image.vendor="Tunner" \
      io.tunner.upstream.image="openbao/openbao:2.7.2@sha256:5046911075e137e154ce49ab051502f4ec7cf367d484ae4a4dce665d9b90561f" \
      io.tunner.source.commit="9e0a0d3c3504a0a00b45e61355afac5e35c78379" \
      io.tunner.security.toolchain="go1.27.3" \
      io.tunner.security.patch="golang.org/x/net@v0.62.0;github.com/go-jose/go-jose/v4@v4.1.6"
