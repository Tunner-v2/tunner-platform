# Preserve the complete vendor stack/configuration/assets and replace only reviewed binaries/plugins.
FROM grafana/otel-lgtm:0.38.1@sha256:0df9bc14142000b161344bd24caf6ddd319b7319055aedaf9b0692b125730af7 AS runtime
COPY --from=grafana /bin/grafana /otel-lgtm/grafana/bin/grafana
COPY --from=grafana /plugins/ /otel-lgtm/grafana/data/plugins-bundled/
COPY --from=tempo /bin/tempo /otel-lgtm/tempo/tempo
COPY --from=tempo /bin/tempo-cli /otel-lgtm/tempo/tempo-cli
COPY --from=tempo /bin/tempo-query /otel-lgtm/tempo/tempo-query
COPY --from=pyroscope /bin/pyroscope /otel-lgtm/pyroscope/pyroscope
COPY --from=obi /bin/obi /otel-lgtm/obi/obi
COPY --from=collector /bin/otelcol-contrib /otel-lgtm/otelcol-contrib/otelcol-contrib
COPY --from=grafana /provenance/grafana/signing-public.json /usr/share/tunner/lgtm-signing-public.json
LABEL io.tunner.scope="dev-test-only" io.tunner.production.authorized="false" io.tunner.upstream.lgtm="0.38.1"
FROM scratch AS provenance
COPY --from=grafana /provenance/ /grafana/
COPY --from=tempo /provenance/ /tempo/
COPY --from=pyroscope /provenance/ /pyroscope/
COPY --from=obi /provenance/ /obi/
COPY --from=collector /provenance/ /collector/
