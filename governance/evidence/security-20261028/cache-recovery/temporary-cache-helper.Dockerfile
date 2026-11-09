# Temporary cache-authentication helper; never a runtime image or source replacement.
FROM golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 AS authenticated
ENV GOTOOLCHAIN=local GOMAXPROCS=2 GOSUMDB=sum.golang.org
WORKDIR /cache-proof
COPY github.com/ /public-proxy/github.com/
RUN echo "c8ba172b5297abf62e50efc8a039e624a5d02b7c5a55c137499e797ffa540a19  /public-proxy/github.com/aws/aws-sdk-go/@v/v1.55.8.zip" | sha256sum -c -
RUN --mount=type=cache,id=tunner-lgtm-go1273-mod,target=/go/pkg/mod,sharing=shared \
    set -eu; go mod init tunner.cache.authentication; mkdir -p /proof; \
    GOPROXY=file:///public-proxy go mod download -json github.com/aws/aws-sdk-go@v1.55.8 > /proof/aws-module-authentication.json; \
    cat /proof/aws-module-authentication.json; cp go.mod /proof/helper-go.mod
FROM scratch AS proof
COPY --from=authenticated /proof/ /
