# Test-only builder: this target has no runtime image or trusted signing key.
FROM golang:1.27.3-bookworm@sha256:5c3e9b43cfd3bb95c7937462e29d87ada57b481a96fa23ad0eb61778d7a00110 AS integration
ENV CGO_ENABLED=0 GOTOOLCHAIN=local
ADD --checksum=sha256:fdd57503b5f591e951ea3dff80ab53a41487c3373dc89370da2e36e7bf752f52 https://codeload.github.com/grafana/grafana/tar.gz/refs/tags/v13.2.4 /tmp/grafana.tar.gz
WORKDIR /src/grafana
RUN tar -xzf /tmp/grafana.tar.gz --strip-components=1
COPY grafana-signature.patch /tmp/tunner-signature.patch
RUN git apply --check /tmp/tunner-signature.patch && git apply /tmp/tunner-signature.patch
COPY verify.go /src/grafana/pkg/plugins/manager/signature/tunnerdev/verify.go
RUN sed -i 's/^package signing$/package tunnerdev/' pkg/plugins/manager/signature/tunnerdev/verify.go
COPY grafana-adapter.go.txt /src/grafana/pkg/plugins/manager/signature/tunner_dev.go
COPY grafana-adapter-test.go.txt /src/grafana/pkg/plugins/manager/signature/tunner_dev_test.go
# No key is trusted in this test-only package. Native adapter tests use an
# isolated in-memory policy; adoption must generate and pin a real dev-build policy.
RUN printf '%s\n' 'package signature' 'import tunnerdev "github.com/grafana/grafana/pkg/plugins/manager/signature/tunnerdev"' 'var tunnerDevPolicy = tunnerdev.Policy{}' > pkg/plugins/manager/signature/tunner_dev_policy.go
RUN --mount=type=cache,id=tunner-lgtm-go1273-mod,target=/go/pkg/mod,sharing=locked \
    --mount=type=cache,id=tunner-lgtm-go1273-build,target=/root/.cache/go-build,sharing=locked \
    go test -p=2 -count=1 -json ./pkg/plugins/manager/signature/... > /tmp/native-signature-tests.json
