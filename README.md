# Coffee Talk Access

A screen reader mod for [Coffee Talk](https://store.steampowered.com/app/914800/Coffee_Talk/),
supporting **both the full game and the demo**. It speaks menus, the profile picker, the
language picker, name entry, the opening cutscene, story dialogue and the brewing screen
through NVDA, JAWS, SAPI or a braille display, and it supplies keyboard navigation for menus
that the game otherwise only lets you drive with a gamepad.

⚠ **Full-game users need v0.7.0 or later.** v0.6.0 was compiled against the demo and hard-binds
`TG_NameKeys.playerNameInput`, which the full game changed from a public `InputField` to a
private `TG_CustomInputField` — so it throws on the name entry screen there.

Installation instructions for players are in [package/README.txt](package/README.txt).

## Status

This project distinguishes **built** from **confirmed working**, because it has repeatedly
found bugs in code that was already marked done — three screens audited, thirteen defects, and
not one of them a missing hook. The bugs were in the words, not the wiring.

- **Working, confirmed live on the demo** — loader and speech, the language picker, main menu,
  options, name entry, the opening cutscene, press-any-key prompts and credit lines.
  Story dialogue is confirmed by the player, though the log that recorded it has since been lost.
- **Built, not yet exercised in game** — brewing (ingredients, glass contents, serve result,
  live stats and hover preview), the chat log, popups, drink recipes, achievements, the
  save/load calendar, gallery, ending epilogues, the newspaper reader, the music app and
  social media.
- **Blocked on the demo, unblocked on the full game** — the smartphone panels. `canOpenSmartPhone`
  is false on the demo, so the phone animates in and is pulled straight back off screen. The gate
  is a scene singleton (`TG_ExpoBuildManager`) the full game never instantiates, and the code is
  otherwise identical, so the app readers should run there unchanged — untested as yet.
- **Verified against the full game, not yet played** — 46/46 types, 102/102 string-bound members
  and 64/64 Harmony targets resolve. That proves the hooks *bind*; it says nothing about whether
  they *say the right thing*, which is where this project keeps finding its bugs.

The gate has moved: it is no longer access to the full game, but **a playthrough of it**. Two
tools automate what used to need a live run — `tools/verify-hook-targets.ps1` (string-bound
lookups, auto-detects which build it is auditing) and `tools/verify-patch-targets.ps1` (reads
`[HarmonyPatch]` attributes out of the compiled DLL, so it cannot drift from the code).

## Building

Requires the .NET SDK and a Coffee Talk install to reference the game assemblies against.

```
dotnet build CoffeeTalkAccess.csproj -c Release
```

By default the build deploys straight into the game's `Mods` folder and copies the native
speech DLLs to the game root — both are load-bearing locations, see the comments in
[CoffeeTalkAccess.csproj](CoffeeTalkAccess.csproj). Pass `-p:SkipDeploy=true` to skip that.

Point the build at a different install with `-p:GameDir="<path>"`.

To produce a distributable zip in `dist/`:

```
.\package.ps1
```

Coffee Talk is a 32-bit game. The native speech DLLs in `libs/` must be the **x86** builds;
`package.ps1` checks the PE machine type and refuses to ship a package that gets this wrong,
because a 64-bit build fails at the first spoken word and nowhere earlier.

## Repository layout

- `src/` — the mod. `Main.cs` is the MelonMod entry point; the subfolders group patches by the
  game system they hook (`Menus/`, `Dialogue/`, `Brewing/`, `FullGame/`, `Speech/`).
- `docs/PLAN.md` — the roadmap, and the file to read first. It says what is next and why.
- `docs/text-sources.md` — read before hooking any new screen. Coffee Talk has four or more
  independent text systems, and a hook on one is invisible to the others; "patched but never
  fires" usually means the wrong system.
- `project_status.md` — verified environment facts and the hard-won rules, each of which cost
  a live run.
- `package/` — the player-facing install guide.
- `libs/` — the x86 native speech DLLs.
- `.retired/` — approaches that were tried and removed, kept so they are not retried.

`decompiled/` is not tracked: it holds decompiled game code, which belongs to Toge Productions.

## Credits and prior art

Speech goes through [UnityAccessibilityLib](https://www.nuget.org/packages/UnityAccessibilityLib)
and in turn UniversalSpeech. Patching is [HarmonyX](https://github.com/BepInEx/HarmonyX) under
[MelonLoader](https://melonwiki.xyz/). The game's dialogue engine is
[Fungus](https://github.com/snozbot/fungus).

There is an earlier, independent Coffee Talk access mod by
[hellblade940](https://github.com/hellblade940/coffee-talk-access-mod/), taking a
clipboard-and-polling approach. No code is shared with it.

Coffee Talk is a game by Toge Productions. This is an unofficial fan-made accessibility mod
and is not affiliated with or endorsed by them.

## License

GNU General Public License v3.0 or later — see [LICENSE](LICENSE).

This covers the mod's own source in `src/`. It does not extend to the third-party binaries in
`libs/` (UniversalSpeech and the NVDA controller client, which carry their own licenses), and
not to Coffee Talk itself, whose code and assets belong to Toge Productions.
