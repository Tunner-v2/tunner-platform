# Local QR capability validation

TUN-P2-008 selects the pinned local MIT encoder in docs/development/THIRD_PARTY_COMPONENTS.json. Original upstream bytes remain unchanged; existing TypeScript5.9.3 emits only an ESM adaptation. Run from repository root:

    node web/tools/build-qr-vendor.mjs --check
    node --test web/tests/totp/qr.test.mjs

The independent decoder is a disposable PUBLIC-FIXTURE-ONLY TEST ORACLE, not an application dependency. Acquire dist/jsQR.js from immutable jsQR commit49a9633931fb8030ac2fc9cecc121d6e5a19f9a3 (npm1.4.0 metadata), retaining its Apache-2.0 LICENSE beside it. Save under artifacts/p2-008-totp-20261110/qr-oracle-jsqr-1.4.0.js or set TUNNER_PUBLIC_QR_ORACLE to the acquired file. The test requires exact SHA256bc40c8a15196236b2314db0856f72ca0b49980cd5413b8c852a7349f5fee0859 before executing it in a no-Node-API VM context with evaluation timeouts. No package install, runtime decoder, CDN or external QR service is used. No decoder maintenance/security clearance is asserted; only frozen public vectors run through it.

Source: https://raw.githubusercontent.com/cozmo/jsQR/49a9633931fb8030ac2fc9cecc121d6e5a19f9a3/dist/jsQR.js
License: https://raw.githubusercontent.com/cozmo/jsQR/49a9633931fb8030ac2fc9cecc121d6e5a19f9a3/LICENSE

Checks decode numeric, alphanumeric, Unicode and explicitly invalid/public provisioning fixtures at220pixels, verify numeric-only output/four-module quiet zones, reject overflow safely and inspect runtime network capability. They do not certify camera scanning, a phone app, live setup, the eventual SVG component rasterization or all maximum-density inputs. Those applicable rendered/real-flow checks remain required before P2-008 acceptance. Real setup values must never be used with this test runner or its output; see sensitive/README.md for the separate protected boundary.
