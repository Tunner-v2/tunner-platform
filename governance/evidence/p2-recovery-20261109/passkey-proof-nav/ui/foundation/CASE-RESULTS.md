# Existing shared-control foundation gate

Source: `35ef6c3b64881cf50833209b739f0e47a43f5679`. Existing `web/playwright.config.ts`, Edge, owned loopback fixture port 5188. Actual final result: **16 passed, 0 failed, 0 skipped, 0 flaky; exit 0; 21.7 seconds**. No source or appdist change. Owned helper ended normally and port 5188 no longer listens.

| # | Actual executed case | Result |
|---:|---|---|
| 1 | Admin shared field idle border at approved reference dimensions | PASS |
| 2 | User: source metrics, structural roles and font load | PASS |
| 3 | User: keyboard focus, controlled typing and no implicit submit | PASS |
| 4 | User: error descriptions and source-aligned focus | PASS |
| 5 | User: publication refresh updates copy and provenance | PASS |
| 6 | Admin: source metrics, structural roles and font load | PASS |
| 7 | Admin: keyboard focus, controlled typing and no implicit submit | PASS |
| 8 | Admin: error descriptions and source-aligned focus | PASS |
| 9 | Admin: publication refresh updates copy and provenance | PASS |
| 10 | axe WCAG 2.2 AA reports no violations | PASS |
| 11 | Narrow width reflows long content without clipping | PASS |
| 12 | Isolated surface roles and TailAdmin bridge survive side-by-side use | PASS |
| 13 | Fonts and rendering use only local assets and no persisted state | PASS |
| 14 | Nested Admin mode does not leak into either surrounding User control | PASS |
| 15 | User: shared validation handles submit, blur, correction, focus and duplicate submission | PASS |
| 16 | Admin: shared validation handles submit, blur, correction, focus and duplicate submission | PASS |

The pre-existing Admin comparison retains its exact test-only owner-container adaptation, 788×42 field and `rgb(97, 105, 117)` idle border expectation. No production Admin styling, exception or approval scope changed.

Evidence: `results.json`, `playwright.log`, `exit-code.txt`, source/run-head records, and seven current PNGs: Admin reference; desktop/narrow fixture; User/Admin validation at desktop/mobile. This is the existing shared-control gate, separate from native passkey verification and Product/release acceptance.
