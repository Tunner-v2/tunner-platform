# Maintained development images

The local development defaults and CI scan matrix use the same exact reviewed image references. PostgreSQL18.6 and Mailpit1.31.6 retain their tested service behavior; OpenBao2.7.1-r2 applies the bounded go-jose4.1.6 security fix. The source recipes are in infra/security-images. Images are built privately from pinned vendor sources; no registry publication or production deployment is authorized.

The development and browser starters prepare only maintained images actually referenced by their selected Compose configuration. Existing matching recipe labels are reused; otherwise the governed builder runs through PowerShell7. This helper is compatible with the Windows PowerShell5.1 launcher and does not change certificate trust, source location, operator .env.local, host ports or unrelated Docker projects. The first source build can take several minutes. Direct docker compose users must prepare the relevant maintained images first with the same helper.

CI always builds maintained candidates and scans the complete final images. Validate-TunnerSecurityImageDefaults.ps1 enforces one-to-one coverage between declared runtime dependencies and the matrix; the existing result gate and HIGH/CRITICAL/ignore-unfixed policy remain enforced. Vulnerable upstream base images are recorded as source/provenance and their prior findings remain in the audit history; the tested materialized runtime images replace those defaults. No unused vulnerable runtime is silently called clean.

LGTM remains on its existing declaration and its required scan remains blocking. The latest vendor candidate0.38.1 has87 fixable HIGH findings. Its signed-plugin boundary requires a separately approved signing design or clean compatible signed vendor artifacts. Do not remove components, allow unsigned plugins, change trust or suppress findings to pass the gate.

Ports remain PostgreSQL25432→5432, OpenBao28200→8200 and Mailpit28025→8025/21025→1025. The isolated browser host remains https://localhost:24443. These are development/test settings, not production provider configuration or release acceptance.
