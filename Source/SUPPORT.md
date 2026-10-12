# After Hours support

Nothing is sent or uploaded automatically.

1. In Phone → Mod Manager → After Hours → Support and reset, enable **Detailed debug logging**.
2. Reproduce the problem once. For co-op, do this on both the host and affected client.
3. Toggle **Create support report**. It resets automatically after writing a JSON report to `Schedule I/UserData/AfterHours/Reports`.
4. Collect that report and `Schedule I/MelonLoader/Latest.log` before another launch replaces the latest log.
5. Include what you expected, what actually happened, which player was host, and the shortest steps to reproduce. Mention whether it happens after a fresh launch or only after a reload/late join.

The report contains this DLL's checksum, game/runtime versions, installed mod names and versions, startup check status, effective/local settings, and recent mod event/error summaries. It intentionally excludes save contents, account IDs and computer names. The general MelonLoader log can include paths or information printed by other mods: review it before posting publicly.

The optional `Collect-Logs.ps1` script gathers the current MelonLoader log and newest After Hours report into a ZIP. Run it with your game directory. It does not collect saves, preferences, screenshots, or upload anything.

```powershell
./Collect-Logs.ps1 -GameDir 'D:\SteamLibrary\steamapps\common\Schedule I'
```

The script prints the local ZIP path. Send it only through a support channel you choose. Disable detailed logging again after collecting evidence.

## What the long checksum means

A **file checksum (SHA-256)** is a fingerprint of the DLL. It is not encryption, a secret, a password or an access code. Matching checksums identify matching builds. Co-op uses it so different local builds do not silently run incompatible settings/logic. Players normally just install the same ZIP and never need to copy the checksum themselves.

## Useful log messages

- `PASS native save checks`: equipment serializer checks passed in memory.
- `PASS native FishNet settings serializer round trip`: the custom packet passed an in-memory native serializer test. This alone does not confirm a multiplayer connection.
- `Peer ... confirmed matching build and settings revision ...`: the host received that peer's acknowledgement.
- `Settings revision ... from host session`: a client applied host settings without writing them over local preferences.
- `Co-op handshake rejected` or `no matching build/settings acknowledgement`: compare installed DLLs/game versions first, then attach both logs.
- `Compatibility check failed`: required patches were rolled back. Do not continue using a save with upgraded equipment until the version mismatch is resolved.

Detailed events are rate-limited, and the report retains at most 200 recent mod events. Errors are counted; repeated identical error contexts log their full exception once per launch.
