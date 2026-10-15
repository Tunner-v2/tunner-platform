# Closed revision committed proof

P0-004-RV002 corrects current ownership resolution under existing Document 03/04 Revision authority. Original P0 and all prior closures remain unchanged. For each owned path, the latest applicable same-scope closed Revision may supply current validation only when its record, evidence and required reviews are committed and its complete native closure proof passes. Ownership is read from the validation candidate. An invalid latest owner cannot fall back to older validation, including an unvalidated rollback that matches original bytes. Prior Revision checks resolve strictly later owners through existing monotonic ordering. Malformed proof fails diagnostically.

The shared functional test program is also owned by completed P1-020 and P1-022. P1-020-RV001 and P1-022-RV001 revalidate only that test module at the same correction candidate. This preserves their original closures and avoids cross-scope waivers. Their runtime behavior and acceptance rules are unchanged.

Pre-commit governance regression passes, including 60 existing RV cases and 15 new committed-proof cases. Final committed validation and native P1 audit results will be recorded in the scope-specific envelopes before closure. The installer automation is already closed under P1-024-RV001; actual provider acceptance still requires the private operator invocation of ./tools/dev/setup-r2.ps1.

