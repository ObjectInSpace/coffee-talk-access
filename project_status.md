# Coffee Talk Access — Project Status

**Last updated:** 2026-08-10.

## ⚠ LATEST (2026-08-10, later): calendar audited; music + social media built

**The calendar audit found four defects** in `CalendarPatches.cs`, code that was already marked
✅ BUILT. Two ran on EVERY open of the load screen: `lastPlayedText` is a field the game never
assigns (it was gating the "is there anything to say" check), and the continue-summary spoke
~0.7 s before the grid took focus with `interrupt:true`, so it and the focused day cut each other
off. The other two were false speech — "no save" for a condition that actually means "past the end
of the story", and a day number one behind every label on screen because `day` is 0-based.

**Three screens audited now (recipes, achievements, calendar), thirteen defects, and NOT ONE was a
missing or misattached hook.** On the calendar the part most expected to be wrong — the parked-label
parent-chain channel — was already right. The bugs keep being in the WORDS, not the wiring.

**Items C and D are built** (`src/FullGame/MusicAppPatches.cs`, `src/FullGame/SocialMediaPatches.cs`),
untested and unreachable on the demo. Building them falsified two claims `docs/PLAN.md` had been
making, both corrected in place:

- ⚠ **"Target the shared base `TG_MusicAppGeneral`" would have produced a hook that never fires.**
  Its methods are empty virtuals — reflection over the shipped DLL shows `virtual=True,
  IL bytes=1` (a bare `ret`) for all four. Harmony patching a virtual does not intercept calls
  dispatched to an override, so the postfix ATTACHES and `VerifyExpectedPatches` reports it GREEN.
  Both concrete classes are hooked instead. **This is rule 1's failure mode arriving through the
  plan document rather than through the code.**
- ⚠ **Social media was filed "blocked twice over"; neither half held.** The detail pane is three
  bounded slots (`for i < 3`), so it needs one announcement rather than a scroll cursor — and the
  measured-empty key prefixes (`socialMedia/`, `news/`, `profile/`) are not the ones it reads.
  Same adjacent-names trap as `news/` vs `newspaperApp/`.

**Verified offline** by reflecting over `Assembly-CSharp.dll`: all seven new hook targets resolve
and are DECLARED ON the concrete types, and all seven members read through the property helper are
properties with **no reflectable field** — `AccessTools.Field` would have returned null silently on
every one. (Also caught: the demo spells its parameter `pauseSog`, a typo in the game.)

## ⚠ Item E: there was no adapter to write — but the investigation found a retail bug

Asked to build E ("drive the readers from inside the real panels") ahead of a retail binary.
Tracing it showed **every part already existed**: `KeyboardNav` calls the game's own
`HandlerControllerPress()`, which is exactly what routes Up/Down to `musicScreenPanel` /
`recipesDrinkScreenpanel` / `socialMediaScreenPanel`.`ButtonInput(...)`; `TG_KeyboardHotkeyManager.
EscapeHandler` already covers all eight `PHONE_*` states; and the readers hook the game's own
methods, so they fire from inside the real panels however those are reached. The readers were never
waiting on a bridge — only on `canOpenSmartPhone`, a DATA condition.

**The investigation was still worth it: it found a live `FocusRecovery` bug that the demo hides.**
The known "brewing buttons announced under a phone label" bug (log `26-8-10_2-21-28`) had been
fixed by checking `canOpenSmartPhone` — but that cured the SYMPTOM on the demo. The cause was
recovering on a screen with no candidates, reachable two other ways:

1. `NeedsSelection` whitelisted `PHONE_SOCMED_ACCOUNT`, `PHONE_NEWSPAPER` and
   `PHONE_NEWSPAPER_DETAIL`, which the game **does not navigate** — `UpButtonPressed`/
   `DownButtonPressed` route to only four phone states. Those three are Scrollbar panes with no
   Selectable to move between. Removed.
2. `FindEntryControl` scanned the **whole scene**, and the phone draws over a live café whose
   brewing buttons stay interactable. On retail (phone open, `canOpenSmartPhone` true) the guard
   does not apply and it fires. Now scoped to the phone's transform — as a **preference with a
   logged fallback**, because the panels are serialized references that say nothing about the
   transform hierarchy, and a hard filter matching nothing would silently kill phone recovery.

**Rule: a fix that makes the symptom disappear on the build you can run is not a fix.** Ask what
the first failing step was and check every other route into it.

## ⚠ LATEST (2026-08-10): the smartphone is BLOCKED on the demo

`canOpenSmartPhone` is false here, so `OpenSmartPhone` takes its ELSE branch — the phone animates
in, `screenBlockPanel` goes up, and `CloseUnaccesablePhone()` pulls it back off screen ~0.3 s
later. **No app panel ever activates**; no `TG_SmartPhoneApps.Open` hook has ever fired in any log.
Phone panel navigation needs the retail binary. **The app CONTENT is still reachable offline** —
see "Phone content from available data" in `docs/PLAN.md`, and the newspaper (14 days of
`newspaperApp/` strings, all present in the demo) is buildable and testable now. That section is
deliberately UNNUMBERED: the phases are a history, and new work no longer extends the numbering.
`docs/PLAN.md` opens with a **Next up** block naming the single next item.

Fixed the same day: `FocusRecovery` was supplying BREWING buttons on `PHONE_HOME` (it now checks
`canOpenSmartPhone` live), and the open line named four unreachable apps.

**The lesson worth carrying: a hook firing is not a screen working.** The `[Phone]` line fired five
times and the phone never opened once. A source comment asserting "Tab opens the phone on the demo"
had been written from a live report of hearing phone words, never checked against a log, and it
misled a later session immediately. Judge the OUTCOME, not the entry.

**Packaging:** `package.ps1` builds `dist/CoffeeTalkAccess-v<version>.zip` (reads the version from
`MelonInfo`, verifies the native DLLs are x86, fails loudly on a missing file). `package/README.txt`
is the player-facing install guide. Build with `-p:SkipDeploy=true` to skip the game-dir copy.

---

**Previous update:** 2026-08-09, end of session 3.
**Read `docs/PLAN.md` FIRST** — it is the phased roadmap and says what to work on next.
Then this file (verified environment + hard-won rules), then `docs/text-sources.md` before
hooking any new screen.

⚠ The session-1 plan was never written down and session 2 drifted into menu work as a result.
`docs/PLAN.md` now exists so that cannot recur — update it when priorities change.

## What this is

Screen-reader mod for the **Coffee Talk demo**
(`D:\SteamLibrary\steamapps\Common\Coffee Talk Demo`).

## Verified environment (all live-confirmed, do not re-derive)

- Unity **2018.4.9f1**, **Mono** backend, **32-bit (PE32/i386)**.
- `Assembly-CSharp` is **v4.0.30319** -> project targets **net472** (NOT net35).
- Unity 2018.4 has **no `UnityEngine.InputLegacyModule`**; `Input` lives in CoreModule.
- **MelonLoader v0.7.1 x86** — copied from the working Phoenix Wright install
  (that game is Unity 2017.4.8 x86, same loader, proven). Log shows `Game Arch: x86`.
- Speech: **UnityAccessibilityLib 2.0.0** (net472) -> **UniversalSpeech**.
  ⚠ The native `UniversalSpeech.dll` + `nvdaControllerClient.dll` must be the **x86** builds and
  must sit in the **GAME ROOT**, not `Mods\`. The build's `DeployToGameMods` target does this.
- Game code is **UNOBFUSCATED**. Dialogue engine is **Fungus** (open source, snozbot/fungus).
- Game language is in registry `HKCU\Software\Toge Productions\CoffeeTalk`, value
  `I2 Language_h3293684300`, a null-terminated UTF8 string. Demo shipped as `Brazil`;
  **set to `English` 2026-08-09**.

## What WORKS (live-confirmed 2026-08-09, session 2)

- Loader boots, mod loads, speech initializes (`Initialize() = True`).
- F8 speech test, backquote repeat, F10 UI/state dump, automatic `[Devices]` report.
- **Language splash screen** — arrow keys move between flags, each announced; Enter selects.
- **Main menu** — arrows/WASD navigate, "PLAY GAME, 1 of 2" / "OPTIONS, 2 of 2"; **Enter activates**.
- **Options screen** — full narration with live values:
  `"LANGUAGE, ENGLISH, 6 of 10"`, `"RESOLUTION, 1920 X 1080, 4 of 10"`,
  `"SFX, slider, 80 percent, left and right to adjust, 1 of 10"`, `"SKIP DIALOGUE, READ"`.
- **Controller works via the game's OWN path**, no mod involvement — see the DS4Windows note.

## ⚠ CONTROLLER: set DS4Windows output to X360

The pad must be on an **X360**-output profile. InControl 1.7.3's `PlayStation4WinProfile` matches
only two exact joystick names with NO regex fallback; `Xbox360WinProfile` has 30 names plus
`LastResortRegex = "360|xbox|catz"`.

ALSO: the raw DualSense must be **hidden in HidHide** (`HID\VID_054C&PID_0CE6&MI_03\...`, cloak
on, DS4Windows whitelisted). Otherwise it appears as a second, unmapped device and WINS
InControl's most-recent-input race for `ActiveDevice` — so `ListenControllers()` polls a device
with no mappings, reads zero, and the game never enters joystick mode. Symptom: face buttons
click but the D-pad does nothing. This was NOT a game or mod bug.

## SESSION 3 (2026-08-09): text-source survey + two new screens

Player report: "it says 'input' but no text", then "several lines before the game started that the
mod did not read." Both diagnosed from code; **neither was a Fungus problem.**

1. **"Input"** = the name-entry screen. `TG_NameKeys.Initialize` focuses the `InputField`, which
   has no caption, so FocusNarrator read the OBJECT NAME. The mod was working and had nothing to
   say. Fixed by `src/Menus/NameEntryPatches.cs`.
2. **Unread lines** = the opening cutscene, a text system that **bypasses Fungus entirely**.
   Fixed by `src/Dialogue/CutscenePatches.cs`.

⚠ **See `docs/text-sources.md`** — the full survey. Coffee Talk has 4+ independent text systems;
a hook on one is invisible to the others. "Patched but never fires" usually means WRONG SYSTEM.

Also this session: unlabeled controls now announce ", unlabeled" rather than passing an object name
off as a caption; `VerifyExpectedPatches` names any expected hook that did not attach.

**All of session 3 is BUILT AND DEPLOYED BUT UNTESTED.** Next live run should confirm from the log:
`[Patch] All 11 expected hooks are live`, then `[Name]` and `[Cutscene/...]` lines.

## ▶ NEXT: dialogue (not yet started)

⚠ **UNVERIFIED as of 2026-08-10 — the log evidence is GONE.** Sessions 3-5 recorded "dialogue
narration WORKS", citing `26-8-9_19-0-8.log:121-128` (`[Hook] SetCharacterName: 'Freya'` ->
`[Speak/Say] Hey DrewBarista, how's the night so far?`).

**That file is no longer in `MelonLoader/Logs/`** (earliest surviving log: `26-8-9_19-11-52`), the
quoted line appears in no log, and **none of the 10 surviving logs contains any `[Speak/Say]` or
`[Hook] SayDialog.Say FIRED` line.** The furthest any surviving run reached is `INPUT_NAME` + the
confirm popup — no run has entered the café.

**What still stands:** the note also records that **the player confirmed hearing it**, which is
independent of the file. So this is "evidence lost", NOT "claim disproved" — treat dialogue as
PROBABLY working but UNCONFIRMED, and re-establish it on the next playtest (it costs nothing: the
same run that tests brewing passes through dialogue anyway).

**Lesson:** the standing rule was "sweep every log before declaring a hook unobserved". Add: **a
citation is only as good as the file behind it — confirm the file still exists.**

⚠ `Say` fires twice per line and `Writer.Write` twice more; the `_lastSpoken` dedup in `SpeakLine`
is what prevents four utterances. Do not remove it as redundant.

Also still unnarrated: the name-entry screen (`TG_NameKeys`, `State.INPUT_NAME`).

## Architecture

- `src/Main.cs` — MelonMod entry. `ApplyPatches()` + verification, F8/F10/backquote keys,
  `ReadControllerState()`.
- `src/Speech/` — `ISpeechOutput` seam, `UalAnnouncer` (UnityAccessibilityLib).
- `src/Dialogue/FungusText.cs` — strips Fungus tags via the **game's own** `TextTagParser.Tokenize`,
  keeping only `TokenType.Words`. Do NOT hand-roll a regex: Fungus has ~28 tag forms.
- `src/Dialogue/SayPatches.cs` — hooks `SayDialog.Say` + `SetCharacterName` + `Writer.Write`.
  UNTESTED (never reached).
- `src/Menus/KeyboardNav.cs` — **the whole keyboard-nav mechanism.** Binds arrows+WASD onto the
  JOYSTICK action set's directional actions, then calls the game's own `HandlerControllerPress()`
  from a postfix on `HandlerKeyboard`. Arrow keys therefore travel the EXACT path the D-pad
  travels — same edge detection, same repeat timing, same per-state routers. Enter is forwarded
  separately to `AButtonPressed`, gated on the EventSystem having no selection.
- `src/Menus/InitScreenFix.cs` — repairs a GAME bug: `CheckRemovedState` has no `INIT_SCREEN`
  branch, so keyboard mode cleared the language picker's selection and never restored it.
- `src/Menus/MenuPatches.cs` — announces main-menu cursor moves (`MouseOverManager` + `MouseHover`).
- `src/Menus/OptionPatches.cs` — announces options/save-load rows via the BASE class
  `TG_UIMenuContent.EventHoverMouse`, plus re-announces after Left/Right adjusts a slider.
- `src/Menus/FocusNarrator.cs` — announces EventSystem-focused controls (the language picker).
- **Retired** to `.retired/`: `MenuCursor.cs` (kept its own second cursor — quit the game once),
  `JoystickBridge.cs` (forced JOYSTICK mode — broke the language picker).

## SESSION 4 (2026-08-09): repeat key + popups

**The repeat key was broken for exactly the text that most needed it.** UnityAccessibilityLib's
`ShouldStoreForRepeat` defaults to storing ONLY `Dialogue` and `Narrator`. Cutscene credits and
the press-any prompts are sent as `TextType.Menu` (they must not be `Dialogue` — that type takes a
`"Speaker: "` prefix), so they were never stored. Live proof, `26-8-9_22-56-25.log`:

```
22:57:39 [Cutscene/Credit] A Game by Toge Productions
22:57:42 [UAL] Nothing to repeat          <- backquote pressed in between
```

Text that appears on a timer and vanishes is the single best case for a repeat key, and it was the
one case repeat did not serve. Fixed in `Main.cs` via `ShouldStoreForRepeatPredicate` — store
everything except `System` (mod chatter must not overwrite the story line the player wanted back).

⚠ **`RepeatLast` with nothing stored was ACTIVELY HARMFUL.** `UalAnnouncer.RepeatLast` called
`SpeechManager.Stop()` and then `RepeatLast()`, which only *logs* "Nothing to repeat" — it speaks
nothing. So the keypress cut off the line being read and replaced it with silence, which a blind
player cannot distinguish from a crashed mod. Now checks for a stored line first and says
"Nothing to repeat." out loud.

⚠ **`[UAL] [2]` in the log is a text CATEGORY, not a priority.** `Dialogue=0, Narrator=1, Menu=2,
MenuChoice=3, System=4`. I initially misread `[0]` on cutscene lines as a bad priority level. The
log now prints names instead of ints (`TextTypeNames`) so this cannot mislead again.

**Popups built** (`src/Menus/PopUpPatches.cs`) — see `docs/PLAN.md` Phase 2 for the mechanism.
Untested; nothing in any log has ever reached one.

## HARD-WON RULES (violating these cost live runs)

1. **MelonLoader does NOT auto-apply `[HarmonyPatch]`.** Call `PatchAll` explicitly and VERIFY
   with `GetPatchedMethods()`. A silent no-op looks exactly like a working build.
2. **Coffee Talk's menus are GAMEPAD-ONLY.** In keyboard mode the game routes to
   `TG_KeyboardHotkeyManager.HandlerKeyboard()`, which reads ONLY RB/LB/Escape/Submit/Confirm/
   SmartPhoneToggle — it NEVER reads Up/Down/Left/Right despite `KeyboardPlayerActions` binding
   them. Arrow-key menu nav exists only on the joystick path. **The mod must supply it.**
3. **`EventSystem.currentSelectedGameObject == null` does NOT mean "no keyboard navigation".**
   The game drives menus with its own `cursorIdx`, invisible to a focus watcher.
4. **Never let two cursors disagree.** The game re-highlights `buttonList[cursorIdx]` from several
   call sites; if our index differs, it announces the WRONG button. This QUIT THE GAME once (the
   player heard "PLAY GAME" while focus was on Exit). Fix: `SyncGameCursorIndex` + `MenuCursor.
   IsCurrentTarget` gating every announcement.
5. **Stale focus strands the player.** Leaving Options left `AmbienceSlider` selected while the
   game was back on MAIN_MENU; the cursor stood down for a dead screen and arrows went dead.
6. **Destructive actions need a confirm**, not just a warning label — a warning attached to the
   wrong label protects nothing.

## Prior art

`https://github.com/hellblade940/coffee-talk-access-mod/` — clipboard+polling mod. Reuse no code;
see memory `coffee-talk-prior-art-mod`. Its 3 useful insights: brewing auto-focus fights you at
ingredient 3, latte art should be skipped, the smartphone is hard.

## Still to do

**Moved to `docs/PLAN.md`** — one ordered list, so priorities cannot drift between the two files.
Name entry is no longer a blocker (built in session 3, untested).

## Reference data (for whoever builds brewing)

- `TG_Static.Ingredients` = none, sugar, mint, greentea, chocolate, milk, tea, ginger, cinnamon,
  lemon, coffee, honey.
- Drink stats: sweetness / bitterness / warmth / coolness.
- Brewing buttons already have wired `Navigation` + auto-focus (`brewingButton.Select()` at
  `glass_value == 3`), so the game navigates by keyboard already — announce, do not rebuild nav.
