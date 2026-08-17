# Coffee Talk Access

A screen reader mod for [Coffee Talk](https://store.steampowered.com/app/914800/Coffee_Talk/),
supporting **both the full game and the demo**. It speaks menus, the profile picker, the
language picker, name entry, the opening cutscene, story dialogue and the brewing screen
through NVDA, JAWS, SAPI or a braille display, and it supplies keyboard navigation for menus
that the game otherwise only lets you drive with a gamepad.

Installation instructions for players are in [package/README.txt](package/README.txt).

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
- `tools/` — `verify-hook-targets.ps1` checks the string-bound lookups still resolve against an
  install; `verify-patch-targets.ps1` reads `[HarmonyPatch]` attributes out of the compiled DLL,
  so it cannot drift from the code.
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

GNU Lesser General Public License v3.0 or later — see [COPYING.LESSER](COPYING.LESSER) and
[COPYING](COPYING). LGPLv3 is written as a set of additional permissions on top of GPLv3, so
both texts together form the license.

This covers the mod's own source in `src/`. It does not extend to Coffee Talk itself, whose
code and assets belong to Toge Productions.

### Third-party components

| Component | Author | License |
| --- | --- | --- |
| [UnityAccessibilityLib](https://www.nuget.org/packages/UnityAccessibilityLib) | LordLuceus | MIT |
| [UniversalSpeech](https://github.com/qtnc/UniversalSpeech) (`libs/UniversalSpeech.dll`) | Quentin Cosendey | MIT |
| [NVDA Controller Client](https://github.com/nvaccess/nvda/tree/master/extras/controllerClient) (`libs/nvdaControllerClient.dll`) | NV Access | LGPL v2.1 |

The two binaries in `libs/` are redistributed unmodified under their own terms. Copies of the
MIT and LGPL v2.1 texts ship with their respective upstream projects at the links above.
