# Reuse the released UI and apply the bounded DOMPurify security repair.
FROM node:24.22.1-bookworm@sha256:5630d96570d4ca4a8523fda3d169d0114554d0f48293e1ae3db6be7c79b42a17 AS ui
WORKDIR /src
RUN git init . && git remote add origin https://github.com/axllent/mailpit.git && \
    git fetch --depth=1 origin a028b01f4a24b4b36bdad95b8e09e070ebf2bad4 && \
    git checkout --detach FETCH_HEAD && \
    test "$(git rev-parse HEAD)" = a028b01f4a24b4b36bdad95b8e09e070ebf2bad4
COPY mailpit-ui.patch /tmp/mailpit-ui.patch
RUN git apply --check /tmp/mailpit-ui.patch && git apply /tmp/mailpit-ui.patch
RUN npm ci
RUN npm run package

# Audit the exact production UI lockfile with the already approved Trivy policy.
FROM aquasec/trivy:0.70.0@sha256:be1190afcb28352bfddc4ddeb71470835d16462af68d310f9f4bca710961a41e AS ui-audit
COPY --from=ui /src/package.json /src/package-lock.json /scan/
# Every actual build must execute the audit against the current database.
ARG UI_AUDIT_EPOCH
RUN --mount=type=cache,id=tunner-security-trivy-db,target=/root/.cache/trivy,sharing=locked \
    test -n "$UI_AUDIT_EPOCH" && trivy fs --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 \
    --list-all-pkgs --format json --output /ui-audit.json /scan

FROM ui AS verified-ui
COPY --from=ui-audit /ui-audit.json /src/tunner-ui-audit.json
RUN node -e 'const r=require("./tunner-ui-audit.json"); if(!r.Results?.some(x=>x.Type==="npm" && x.Packages?.length>0)) throw Error("Missing production NPM coverage"); console.log("PASS: production NPM lockfile coverage and vulnerability gate");'

FROM golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 AS builder
WORKDIR /src
COPY --from=verified-ui /src/ /src/
ENV CGO_ENABLED=0 GOTOOLCHAIN=local
RUN go mod download && go mod verify && \
    go install github.com/google/go-licenses/v2@v2.0.1 && \
    go-licenses report ./... --template .github/third-party-licenses.tpl --ignore github.com/axllent/mailpit > internal/licenses/third-party.txt
ARG TARGETARCH
RUN --mount=type=cache,id=tunner-security-go1273,target=/root/.cache/go-build,sharing=locked \
    GOOS=linux GOARCH=$TARGETARCH go build -p=2 -trimpath \
    -ldflags='-X github.com/axllent/mailpit/config.Version=1.31.6' -o /out/mailpit . && \
    go version -m /out/mailpit

FROM axllent/mailpit:v1.31.6@sha256:23fe39e81bde596a918000b3135c0f85dc8bb4c9ea6be274b051143f3e300387
COPY --from=builder --chmod=755 /out/mailpit /mailpit
COPY --from=verified-ui /src/tunner-ui-audit.json /usr/share/tunner-security/mailpit-ui-dependency-audit.json
LABEL org.opencontainers.image.title="Tunner security rebuild of Mailpit 1.31.6" \
      org.opencontainers.image.vendor="Tunner" \
      io.tunner.source.commit="a028b01f4a24b4b36bdad95b8e09e070ebf2bad4" \
      io.tunner.security.toolchain="go1.27.3" \
      io.tunner.security.ui.patch="dompurify@3.4.17"
