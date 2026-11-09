# LGTM build cache concurrency

The independent component builds contended on two exclusive BuildKit cache mounts. The Collector dependency resolver remained silent for over30 minutes while the other builds queued; this does not establish a particular Go or BuildKit defect. Retain the interrupted logs.

Use task-private shared Go module/build caches and Go's own per-file concurrency controls. The official module reference explicitly permits simultaneous Go commands, and pinned Go1.27.3 `go help cache` confirms concurrent build-cache access. Keep read-only downloaded module source, default checksum-database authentication, `go mod verify`, exact source sums and all tests/scans. No insecure-module, checksum-disable, CVE-ignore or unsigned-plugin switch is introduced. Limit each builder to GOMAXPROCS2 and package build parallelism2.

Primary source: https://go.dev/ref/mod#module-cache ; local pinned help receipt: artifacts/security-followup-20261028/go-cache-help.txt. Only task-owned build clients are interrupted during recovery; Docker, operator containers/volumes and pre-existing caches are preserved. Complete image/compatibility/CI validation remains required.
