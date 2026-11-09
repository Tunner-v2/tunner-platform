# Patch the existing vendor OS through its own signed RHEL9 package repositories.
# The temporary installer root excludes application payloads; runtime inherits the complete unchanged vendor stack.
FROM grafana/otel-lgtm:0.38.1@sha256:0df9bc14142000b161344bd24caf6ddd319b7319055aedaf9b0692b125730af7 AS vendor
FROM redhat/ubi9:9.8@sha256:217dd5692d369799d34d09ffb6f5f3404efebc7900834490fdf9ae92fd6721d4 AS repair
RUN --mount=type=bind,from=vendor,source=/,target=/vendor,readonly \
    set -eu; mkdir -p /target /out/rootfs /out/provenance/os; \
    for item in /vendor/* /vendor/.[!.]* /vendor/..?*; do \
      test -e "$item" || test -L "$item" || continue; name="${item##*/}"; \
      test "$name" = otel-lgtm && continue; cp -a -- "$item" "/target/$name"; \
    done; \
    dnf --installroot=/target --releasever=9 --setopt=reposdir=/etc/yum.repos.d --setopt=gpgcheck=1 --setopt=install_weak_deps=False upgrade -y libbrotli-1.0.9-10.el9_8.1 > /out/provenance/os/dnf-transaction.log 2>&1 || { cat /out/provenance/os/dnf-transaction.log; exit 1; }; \
    test "$(rpm --root /target -q --qf '%{VERSION}-%{RELEASE}' libbrotli)" = 1.0.9-10.el9_8.1; \
    rpm --root /target -qi libbrotli > /out/provenance/os/installed-package.txt; \
    rpm --root /target -V libbrotli > /out/provenance/os/rpm-verify.log; \
    rpm --root /target -ql libbrotli > /out/provenance/os/package-files.txt; \
    cd /target; \
    while IFS= read -r file; do \
      case "$file" in /*) ;; *) echo 'Non-absolute RPM package path'; exit 1;; esac; \
      case "/$file/" in */../*) echo 'Unsafe RPM package path'; exit 1;; esac; \
      relative="${file#/}"; if test -f "$relative" || test -L "$relative"; then cp -a --parents -- "$relative" /out/rootfs; fi; \
    done < /out/provenance/os/package-files.txt; \
    copied_database=false; \
    for database in var/lib/rpm usr/lib/sysimage/rpm; do \
      if test -e "$database" || test -L "$database"; then cp -a --parents -- "$database" /out/rootfs; copied_database=true; fi; \
    done; test "$copied_database" = true; \
    find /out/rootfs -type f -exec sha256sum '{}' ';' > /out/provenance/os/exported-files.sha256
FROM scratch AS candidate
COPY --from=repair /out/ /
LABEL io.tunner.scope="os-package-repair-candidate-not-runtime-accepted"
