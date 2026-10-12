# After Hours: Industry & Motion

**0.4.2 — co-op test release · by After Hours Project**

Upgraded production equipment and an electric skateboard for Schedule I. One DLL, embedded visuals, and optional in-game controls through Mod Manager & Phone App.

Built for **Schedule I 0.4.7f12, default IL2CPP branch**, with **MelonLoader 0.7.3**. The alternate Mono branch is not supported by this build. Other game versions are unverified.

## What it adds

| Upgrade | Seller | Default price | Default behavior |
| --- | --- | --- | --- |
| Chemistry Station Mk II | Oscar's Store | $3,000 | Two-hour liquid batches; two liquid outputs per normal output; one quality tier higher, capped at Heavenly; automatic container disposal |
| Lab Oven Mk II | Oscar's Store | $3,000 | One-hour liquid batches; 20 meth per liquid; preserves liquid quality |
| Circuit Electric Skateboard | Skateboard Shack | $3,000 | 44.8 km/h, electric acceleration, responsive steering and braking, stronger jumps, no native terrain slowdown, assisted small curb climbs |

Regular input quality becomes Premium/purple liquid by default; high-quality input becomes Heavenly/gold. The oven preserves that grade. Workers retain native assignment and supply rules, with faster setup/harvest waits. Ordinary equipment retains its original behavior.

Both benches keep their native footprint, controls, collision and working parts, with graphite/steel finishes and subtle teal trim. Circuit has an original procedural deck, trucks, wider wheels and battery pack. Curb assistance checks clearance; it cannot climb walls or remove collisions.

## Install

1. Back up the save you intend to test. Fully exit the game.
2. Install MelonLoader for the game's **IL2CPP** branch and run it once.
3. Extract the runtime ZIP into the game folder so the DLL is at `Schedule I/Mods/AfterHours.dll`.
4. If upgrading from 0.4.1 or earlier, remove the old `ChemistryStationMkII.dll` from Mods. Keep only `AfterHours.dll`; installing both loads the same mod twice.
5. Launch. Look for **After Hours** and the native save-check PASS lines in `MelonLoader/Latest.log`.

Only the DLL is required for gameplay. No S1API or networking helper dependency is required by this mod. Mod Manager & Phone App is optional for the phone controls; its 2.2.4 API was inspected for this integration. Its own dependencies must also be installed if you use it. Without the phone app, MelonLoader preferences remain available in `UserData/MelonPreferences.cfg` after launch; edit that file only while the game is closed.

The ZIP uses a standard `Mods/` layout. Manual installation is supported; installation through Vortex has not been verified.

## In-game settings

Open **Phone → Mod Manager → After Hours**. Available controls include:

- Chemistry and oven cook times, quantities, quality increase or fixed grade, worker speed, and prices.
- Chemistry automatic trash disposal.
- Board top speed, acceleration, jump force, steering strength/response/return, grip, braking, motor, terrain slowdown, curb height/smoothing/cooldown, and price.
- Detailed logging, support-report creation, and reset to defaults.

**Values apply when you finish editing a field. Click the manager's Save button to retain them for future launches.** The warning appears in the mod's settings. Changes to cook time, yield and grade may affect batches already running. Finished items stay unchanged. Larger yields must fit the full output slot capacity; some choices require a stack-size mod. Lower the yield if production is waiting for space. Price changes apply when reopening a shop; they do not refund past purchases.

Durations are in game minutes. Other recipes scale proportionally to the native liquid recipe. Fractional processing progress is carried while playing; less than one native recipe minute of fractional remainder is discarded on reload. Native whole-minute progress remains saved normally.

## Co-op test instructions

**Co-op support is experimental until the two-player checklist in COOP-TEST.md is completed.** Automated and in-memory checks do not establish network correctness under real play.

Every player, including the host, must use the **exact same DLL file** and game version. Use the supplied runtime ZIP on every PC. Independently rebuilding source may create a different file fingerprint even with the same version number. Keep other gameplay mods compatible across the group as well.

The host controls gameplay settings for the session. Clients acknowledge the host manifest before this mod releases deferred player inventory initialization. Live host changes are resent until acknowledged. Missing or mismatching acknowledgements cause a disconnect after a loading grace period of up to three minutes. A modded client joining an unmodded host is also unsupported. The disconnect reason appears in `Latest.log`; there is no custom disconnect popup.

Host values remain temporary on a client: they never overwrite the client's saved preferences. Client setting fields show their local preferences, not editable host controls. The generated support report records both effective and local values. Reopen the settings page to refresh the informational session status.

Production continues through the game's native ownership and RPC paths. Chemistry output creation remains server-side; client completion cleanup is not blocked by client inventory capacity. Electric drive applies only to the board's owner. There is no custom production network prefab or separate save system.

Do not describe this preview as universally multiplayer compatible. Late joins, simultaneous interactions, worker production, time skips, remote board appearance, and reconnects still require real co-op testing. See COOP-TEST.md.

## Save compatibility and removal

Finish batches, dismount, save, and fully exit before changing the installed DLL. Keep a separate backup.

Equipment saves retain native chemistry station, oven and golden skateboard fallback IDs, plus optional upgrade markers. With the mod installed, those markers restore the upgrades. Without it, the equipment can load as its ordinary counterpart; native finished product qualities remain native qualities.

**Saving without the DLL can permanently discard upgrade markers.** Reinstalling does not recover markers that the base game already discarded. Native equipment values apply to downgraded items. All players must use matching installed state in co-op. The mod does not edit old save files directly or restore backups automatically.

Internal item identifiers are retained from earlier previews for save compatibility. Version 0.4.2 renames the public DLL to `AfterHours.dll`; the filename is not part of the save format. The public project name and author are After Hours and After Hours Project.

## Heavenly quality

The mod uses the game's native Heavenly tier, rather than inventing a new economy. Inspection of the supported game build found customer quality scoring and delivery quality bonuses already recognize it. Enjoyment gains depend on customer standards and existing caps; a customer already at their cap may gain nothing. Native delivery calculations can award a quality bonus when the delivered grade exceeds the requested grade; dealer commission still applies.

This is not a blanket increase to listed product prices. No duplicate payouts or custom relationship rewards are added. The mod logs a read-only quality comparison after loading. Actual sale/payment outcomes remain a gameplay test item.

## Reporting a bug

See **SUPPORT.md**. Enable detailed logging before reproducing the issue, then create a support report from the phone settings. Share the report and `MelonLoader/Latest.log` from both computers for co-op issues, with the steps that triggered it. Nothing is uploaded automatically.

## Build from source

Install .NET SDK 8 or newer. Run MelonLoader once to generate the installed game's IL2CPP assemblies. From this source folder:

```powershell
dotnet build ChemistryStationMkII.csproj -c Release '-p:GameDir=D:\SteamLibrary\steamapps\common\Schedule I'
dotnet run --project tests/Tests.csproj -c Release '-p:GameDir=D:\SteamLibrary\steamapps\common\Schedule I'
```

Replace the example with your own game folder. The default project path is Steam's standard Program Files (x86) location. The build output is `bin/Release/net6.0/AfterHours.dll`. Distribute the same built DLL to every tester. Do not redistribute game assemblies or MelonLoader dependencies with it.

Source, custom mesh data, icons, and tests are included. See LICENSE.txt for project code/assets; the game and its trademarks belong to their respective owners.
