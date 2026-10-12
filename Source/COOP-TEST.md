# Two-player release checklist

Status: NOT COMPLETED. Use a backed-up test save and the same runtime ZIP/game version on both computers. Keep both logs after every failed case. Passing source tests and a single-player launch does not satisfy this checklist.

- Host starts a save; client joins. Confirm host log acknowledges matching build/settings and client receives host values.
- Give clients different local preferences before joining. Host values must govern gameplay; after leaving, local preferences must return unchanged. Check both support reports.
- Each player buys, places, picks up and replaces both benches. Counts, money and ownership must agree. Native equipment must remain ordinary.
- Each player runs a regular-quality chemistry batch: two Premium liquid outputs in two hours by default. Repeat with high-quality input: Heavenly liquid. Check no duplicate output or stuck client visuals.
- Each player harvests an oven batch: twenty products per liquid in one hour, preserving the liquid grade. Test a Heavenly batch and a non-default yield.
- Block output with a full or mismatched-grade stack. Clear it and verify exactly one completed batch is collected, with no consumed output lost.
- Assign a worker to both benches, then sleep through a batch. Verify counts and timers on both PCs.
- Change host cook times/yields and board speed; confirm the client applies the new revision. Return to defaults. Changes may affect running batches by design.
- Save/reload with a batch running, with output waiting, and with benches/board in inventory. Test a late join in each state.
- Ride Circuit while the other player observes. Test steering, curbs, rain, dismounting and remote appearance. Native boards must retain native behavior.
- Disconnect and reconnect a client; return both players to the menu and change who hosts. No stale settings or duplicate output should remain.
- On a disposable session, test a different/missing mod build. The incompatible join should be rejected, and the host save must remain intact. Never use your only save for this test.
- Generate reports on both PCs. Confirm they contain effective settings, matching checksums and useful session/error information.

Record actual results, game build, DLL checksum, host/client roles, and other installed gameplay mods. Do not mark a case passed based solely on code inspection.
