# Reuse the released source distribution, including its built embedded UI.
FROM golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 AS builder
ADD --checksum=sha256:6da0c07e61d8dfd2ab72ad47cb96691fb7abfecd460a55272545346575c37711 https://github.com/openbao/openbao/releases/download/v2.7.1/openbao-dist-v2.7.1.tar.xz /tmp/source.tar.xz
WORKDIR /src
RUN apt-get update && apt-get install -y --no-install-recommends xz-utils=5.4.1-1+deb12u2 && rm -rf /var/lib/apt/lists/*
RUN tar -xJf /tmp/source.tar.xz --strip-components=1 && test -f internal/http/web_ui/index.html
ENV CGO_ENABLED=0 GOTOOLCHAIN=local
RUN go get golang.org/x/net@v0.60.0 github.com/go-jose/go-jose/v4@v4.1.6 && go mod vendor && go mod verify
ARG TARGETARCH
RUN --mount=type=cache,id=tunner-security-go1273,target=/root/.cache/go-build,sharing=locked GOOS=linux GOARCH=$TARGETARCH go build -p=2 -trimpath -tags=ui \
    -ldflags='-X github.com/openbao/openbao/v2/internal/version.fullVersion=2.7.1 -X github.com/openbao/openbao/v2/internal/version.GitCommit=a5db72cef75c24b920ade02065b18dd8eb666bac -X github.com/openbao/openbao/v2/internal/version.CommitDate=2026-10-01T13:32:54Z -X github.com/openbao/openbao/v2/internal/version.VersionMetadata=tunner.security2' \
    -o /out/bao . && go version -m /out/bao

FROM openbao/openbao:2.7.1@sha256:6d2b93856e3fcf7b18ad855a0b51eaba474dc8b79cf554379ea32034797d2acf
COPY --from=builder --chmod=555 /out/bao /usr/bin/bao
LABEL org.opencontainers.image.title="Tunner security rebuild of OpenBao 2.7.1" \
      org.opencontainers.image.vendor="Tunner" \
      io.tunner.upstream.image="openbao/openbao:2.7.1@sha256:6d2b93856e3fcf7b18ad855a0b51eaba474dc8b79cf554379ea32034797d2acf" \
      io.tunner.source.commit="a5db72cef75c24b920ade02065b18dd8eb666bac" \
      io.tunner.security.toolchain="go1.27.3" \
      io.tunner.security.patch="golang.org/x/net@v0.60.0;github.com/go-jose/go-jose/v4@v4.1.6"
