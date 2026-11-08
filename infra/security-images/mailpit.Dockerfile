# Preserve the complete current vendor runtime; independently audit its exact released production UI graph.
FROM aquasec/trivy:0.70.0@sha256:be1190afcb28352bfddc4ddeb71470835d16462af68d310f9f4bca710961a41e AS ui-audit
ADD --checksum=sha256:d4b813d34faeed3fdfc9c4614aaefc6c4fc42ed467eabdabeb508d273950ee06 https://raw.githubusercontent.com/axllent/mailpit/3bc38efc5c4687659b9099eb2c8d8f6e76113f11/package.json /scan/package.json
ADD --checksum=sha256:b4325c24efede5de5401f88b16c1bd73cf794e979191d4c02012b71836e924f1 https://raw.githubusercontent.com/axllent/mailpit/3bc38efc5c4687659b9099eb2c8d8f6e76113f11/package-lock.json /scan/package-lock.json
ARG UI_AUDIT_EPOCH
RUN --mount=type=cache,id=tunner-security-trivy-db,target=/root/.cache/trivy,sharing=locked \
    test -n "$UI_AUDIT_EPOCH" && trivy fs --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 \
    --list-all-pkgs --format json --output /ui-audit.json /scan || { cat /ui-audit.json; exit 1; }

FROM axllent/mailpit:v1.33.1@sha256:aef2aacc11f075ec0fb6874275ee4a421e688e11a96ebb296134d1735c7761ce
COPY --from=ui-audit /ui-audit.json /usr/share/tunner-security/mailpit-ui-dependency-audit.json
LABEL org.opencontainers.image.title="Tunner audited vendor Mailpit 1.33.1" \
      org.opencontainers.image.vendor="Tunner" \
      io.tunner.source.commit="3bc38efc5c4687659b9099eb2c8d8f6e76113f11" \
      io.tunner.upstream.image="axllent/mailpit:v1.33.1@sha256:aef2aacc11f075ec0fb6874275ee4a421e688e11a96ebb296134d1735c7761ce" \
      io.tunner.security.mode="vendor-binary-and-ui-unchanged-with-independent-production-lockfile-audit"
