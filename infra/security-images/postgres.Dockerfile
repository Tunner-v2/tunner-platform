# Preserve the baseline PostgreSQL 18.6 Debian image; rebuild only upstream gosu 1.19.
FROM golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 AS builder
WORKDIR /src
RUN git init . && git remote add origin https://github.com/tianon/gosu.git && \
    git fetch --depth=1 origin 6456aaa0f3c854d199d0f037f068eb97515b7513 && \
    git checkout --detach FETCH_HEAD && \
    test "$(git rev-parse HEAD)" = 6456aaa0f3c854d199d0f037f068eb97515b7513
ENV CGO_ENABLED=0 GOTOOLCHAIN=local
RUN go mod download && go mod verify && go test ./...
ARG TARGETARCH
RUN GOOS=linux GOARCH=$TARGETARCH go build -trimpath -ldflags='-d -w' -o /out/gosu . && \
    go version -m /out/gosu

FROM postgres:18.6@sha256:5a5a84b19854a9ffaa54082c166ff4ec27473a361e496e5ea167f298f2da9722
RUN apt-get update && apt-get install -y --no-install-recommends --only-upgrade \
    libpcre2-8-0=10.46-1~deb13u3 libssl3t64=3.5.7-1~deb13u3 \
    openssl=3.5.7-1~deb13u3 openssl-provider-legacy=3.5.7-1~deb13u3 && \
    rm -rf /var/lib/apt/lists/*
COPY --from=builder --chmod=755 /out/gosu /usr/local/bin/gosu
COPY --from=builder /src/LICENSE /usr/local/share/licenses/tunner-gosu/LICENSE
LABEL org.opencontainers.image.title="Tunner security rebuild of PostgreSQL 18.6" \
      org.opencontainers.image.vendor="Tunner" \
      io.tunner.upstream.image="postgres:18.6@sha256:5a5a84b19854a9ffaa54082c166ff4ec27473a361e496e5ea167f298f2da9722" \
      io.tunner.gosu.source="https://github.com/tianon/gosu/commit/6456aaa0f3c854d199d0f037f068eb97515b7513" \
      io.tunner.security.toolchain="go1.27.3"
