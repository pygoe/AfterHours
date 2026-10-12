# Changelog

## 0.4.2 - public DLL filename

- Rename the distributed assembly to AfterHours.dll. Upgrading users must remove ChemistryStationMkII.dll to avoid duplicate loading.
- Internal item IDs, save markers, settings category IDs and gameplay are unchanged.

## 0.4.1 — board visual hotfix

- Electric boards now detach their original visual meshes per instance as well as hiding the native renderers, preventing the golden model from showing underneath Circuit. Physics colliders, controls, original boards and shared prefabs remain intact.
- Neutral public author metadata and mapped compiler paths exclude personal build locations.
- Mod menu appearance confirmed in live testing; normal ovens intentionally retain their native appearance.

## 0.4.0 â€” co-op test release

- Public branding: After Hours: Industry & Motion, by After Hours Project. Save identifiers and DLL filename retained.
- Phone Mod Manager controls for bench production, quality, worker speed, prices, waste disposal and board movement.
- Input validation, reset to defaults, visible live-batch/capacity/co-op warnings, and fractional cook-time accumulation.
- Host settings manifest and acknowledgements over native FishNet; matching DLL/game checks; temporary client overrides; deferred player inventory initialization.
- Chemistry completion cleanup no longer depends on a client's potentially newer output-slot state.
- Opt-in detailed events, bounded recent-event history, local support reports, and a log collection script.
- Public packaging, source/build instructions, and explicit co-op test checklist.

## 0.3.1

- Circuit board model and icon, graphite/steel bench finishes and icons, shop dialogue positioning, eased curb assistance.

## Earlier previews

- Chemistry Station Mk II, Lab Oven Mk II, automatic chemistry trash disposal, improved timers, native Heavenly products and electric skateboard gameplay.
