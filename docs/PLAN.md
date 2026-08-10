# Coffee Talk Access — the plan

**Goal: a blind player can complete Coffee Talk unaided.** The demo is the staging ground; the
full game is the destination.

> **Provenance.** A phased plan was agreed in session 1 but never written to a file — it survived
> only as the "PHASE 0" comment in `Main.cs:19` and the "Still to do" list in `project_status.md`.
> Session 2 went deep on menus and the plan drifted. This file is the reconstruction, with session
> 3's text-source survey folded in. Items marked **[recovered]** trace to a session-1 artifact;
> **[new]** items come from the `docs/text-sources.md` survey. Nothing here is discarded work — the
> ordering is a proposal, and the phase boundaries are the part most worth arguing with.

## ▶ Next up (2026-08-10)

**The newspaper reader is BUILT** (item **A**, `src/FullGame/NewspaperAppPatches.cs`) — but read
the ⚠ on item A before touching it: the player chose to hook only the game's own app rather than
take a mod hotkey, so **it is unreachable on the demo and has never executed.** The 14 days of
`newspaperApp/` strings are confirmed present in the demo's assets, and the paragraph splitter was
validated offline against those real bytes, but no line of the class has run in the game.

**Item B (recipes audit) is DONE — see below. It found four real speech defects in code that was
already "built", including a tab announcement that named two tabs after the wrong thing.**

**The ACHIEVEMENTS audit is DONE (2026-08-10) — five more defects, in the phase-5 screen most
likely to actually fire on the demo.** Two produced actively false speech: an icon could announce
"unlabeled" while its real phrase leaked onto the next control, and an unlocked achievement with a
missing localization key was announced as "Hidden achievement, locked". See Phase 5 below.
**The audit pass is not optional bookkeeping — it is where the bugs are.**

**The CALENDAR audit is DONE (2026-08-10) — four more defects. THREE SCREENS AUDITED, THIRTEEN
DEFECTS, and not one of them was a missing or misattached hook.** Two of the calendar's four ran on
EVERY open of the load screen, not just an edge case; and the part of that screen most expected to
be wrong (the parked-label channel that had just been found misrouted on achievements) was the one
part already right — the bugs were in the WORDS instead. See Phase 5 below.
**Eight screens still carry a ✅ earned only by hooks attaching.**

**Items C (music) and D (social media) are now BUILT (2026-08-10, untested)** — so the phase-5
backlog no longer has unwritten code in it except E (adapter), which needs the panels to open.
Building them falsified two claims this file had been making; both are corrected in place below,
and the music one was the dangerous kind — following it produces a hook that attaches, reports
GREEN, and never fires.

**Next: a PLAYTEST is the highest-value move, and achievements are the reason.** They hang off the
MAIN MENU's extras screen, so unlike the rest of phase 5 the demo can reach them — and their five
fixes are unverified, as are the calendar's four. The same run covers everything else never
exercised (brewing, chat log, stats preview, popups, and dialogue's lost receipt).

Do not let the phase-5 backlog imply the project is gated on unwritten code — **it is gated on
ACCESS, and on verifying what exists.** That was true when C and D were unbuilt and it is more
true now: the ratio of untested-to-tested surface just grew again.

## Nothing further is worth BUILDING (2026-08-10)

The remaining `⬜` markers in this file are not a code backlog. Accounted for individually:

- **E (adapter) — INVESTIGATED, and there was nothing to write.** Every part of it already
  existed: `KeyboardNav` calls `HandlerControllerPress()`, which is what routes Up/Down to the
  phone panels; `EscapeHandler` covers all eight `PHONE_*` states natively; and the readers hook
  the game's own methods. **The investigation was still worth doing** — it surfaced a live
  `FocusRecovery` bug that would have fired on the retail build (see below). Tracing what a feature
  must do can be the deliverable even when no feature gets written.
- **News detail pane** — genuinely open, but unmeasured on its own terms (it was bucketed with
  social media, and that bucket's verdict turned out to be wrong for the other half).
- **Rolling credits** — not buildable, and this negative HAS a positive control: the same grep that
  found no `.text` writer here did find the epilogue's. The strings are authored in the scene.
- **Phase 6 (four items)** — all VERIFICATION against the retail assembly, not code.

**So the next action is a live run, not an edit.** Four features (A newspaper, B recipes, C music,
D social media) have never executed a single line in the game, and thirteen audit fixes across
three screens are unconfirmed. Writing more would deepen that hole; running the game fills it.

⚠ **The audit's own lesson: "built" and "correct" are different claims, and this project has been
tracking only the first.** Every phase-5 item below is marked ✅ BUILT on the strength of hooks
attaching. Item B was the first one actually read back line by line, and four of its announcements
were wrong. Budget an audit pass per screen before calling any of them done.

Where everything else stands:

- **Phases 0-4 — done, and working on the demo** (foundation, menus, entry, dialogue, brewing).
- **Phase 5 — built, partly unconfirmable.** Recipes, achievements, calendar, gallery, endings and
  newspaper hooks exist; the **smartphone panels are blocked on the demo by design**
  (`canOpenSmartPhone == false`) and cannot be exercised here.
- **Phase 6 — blocked on a retail binary**, not on unwritten code.

So the project is gated far more on ACCESS to the full game than on remaining work. After item A,
the next confirmable thing is item B (recipes audit); everything past that grows untested surface.

## Standing rules

These are not aspirations; each was paid for with a live run. See `project_status.md`
"HARD-WON RULES" for the full list, and these two above all:

- **Verify hooks attach.** `PatchAll` is not enough — `VerifyExpectedPatches` names any expected
  hook that did not. A silent no-op is indistinguishable from a working build.
- **A silent stop is the worst failure mode for a blind player.** Prefer an announcement that says
  something is missing over one that says nothing. Unlabeled controls say ", unlabeled" for this
  reason — a bare object name reads as a real label and hides the gap.

## Phase 0 — foundation ✅ DONE [recovered]

Loader boots, speech reaches NVDA, Harmony patches verified live. F8 test, backquote repeat,
F10 state dump.

## Phase 1 — menus ✅ DONE (session 2) [recovered]

Language picker, main menu, options, save/load. Keyboard nav supplied by `KeyboardNav`, since the
game's menus are gamepad-only. Controller works via the game's own path.

## Phase 2 — getting into the game ✅ CONFIRMED LIVE (session 4)

Everything between "PLAY GAME" and the first line of story. This phase exists because the player
hit it live: the name screen said only "Input", then the cutscene went unread.

- ✅ Name entry (`TG_NameKeys`) — field, prefill, per-character edits, prefill-wipe, 12-char cap,
  empty-name error. **[recovered — was listed as a BLOCKER]**
- ✅ Opening cutscene (`TG_CutsceneManager`) — bypasses Fungus entirely. **[new]**
- ✅ Press-any-key prompts, cutscene credit lines. **[new]**
- ✅ **Popups (`TG_PopUpManager`) — BUILT session 4, untested.** `src/Menus/PopUpPatches.cs`.
  The JOYSTICK gate is worse than "may never take focus": `SelectButtonPopUpConfirmation:66` and
  `SelectButtonPopUpLoad:98` are *entirely* inside it, so on keyboard **nothing is ever selected**
  while a popup is open. Structural, not a timing issue — hence direct text hooks.
  - Keyboard has **no cursor between Yes and No**; they are separate keys
    (`ConfirmButtonHandler` -> Yes, `HandlerBButton`/Escape -> No). We announce
    "Press Enter for yes, Escape for no" — announcing arrow nav would describe a control that
    does not exist.
  - Announcement is **deferred to a watcher**: text is set before the dialog is visible and
    `DoSetActiveDelay:130-135` activates/deactivates/reactivates across two frames.
  - `TG_NameKeys.InitPopUp:97` pre-registers a popup with `""` and `showPopUp:false`, so empty
    text must never arm the watcher.
  - Dismissing back to `INPUT_NAME` now re-announces the field and the current name; the game
    does that silently.

**Validated live 2026-08-09** (`26-8-9_23-2-34.log`): `[Patch] All 11 expected hooks are live`,
`[Name]` lines through the whole edit flow, `[Cutscene/Credit]` + `[Cutscene/Opening]`. The
60-second silent window from session 3 is closed. Popups are the only part of this phase not yet
exercised by a run (the player answered the confirm before the hooks existed).

⚠ **The `26-8-9_19-0-8.log` cited by earlier sessions NO LONGER EXISTS** (see the Phase 3 warning
below). The name-screen diagnosis it supported is nonetheless still sound: `[Focus] Input Field`
lines appear in the surviving logs, and the fix is confirmed by the `[Name]` lines in
`26-8-9_23-2-34.log`.

## Phase 3 — story dialogue [recovered]

The core of the game.

- ✅ **DIALOGUE NARRATION IS CONFIRMED WORKING — by the PLAYER, who has now said so twice.**
  Do not put this back on a to-verify list. Do not ask for it to be re-tested.
  - The supporting log (`26-8-9_19-0-8.log`) has since rotated away, and no surviving log contains
    a `[Speak/Say]` line. **That is a lost receipt, not a counter-claim.** A whole-corpus sweep
    answers "is there a FILE proving it" — a different question from "does it WORK", and the
    player's first-hand report answers the second one directly.
  - ⚠ **This hook's status has now been misreported FOUR times** (twice as "never fires", once as
    "no run reached dialogue", once as "unverified, re-establish it" — the last of those AFTER a
    correct whole-corpus sweep). Each write-up made the next session more confident and more wrong.
    See [[coffee-talk-sweep-all-logs]]: a user's report of observed behaviour is primary evidence
    and does not expire when its log rotates.
  - ⚠ Still true and worth keeping: **no SURVIVING log shows a run inside the café** (states in the
    logs: `INIT_SCREEN`, `MAIN_MENU`, `OPTION_MAIN_MENU`, `TWEENING`). So brewing and the chat log
    remain genuinely unexercised — but that is a statement about *those* systems, not about
    dialogue, and the two must not be bundled again.
- ⚠ `Say` fires TWICE per line and `Writer.Write` fires twice more; the `_lastSpoken` dedup in
  `SpeakLine` is what keeps the player from hearing it four times. Load-bearing, not defensive.
  (This is read from the SOURCE, and stands independently of the missing log.)
- ❌ **Dialogue choices — CLOSED AS A DEAD END (session 5). Coffee Talk has no choice system.**
  `grep -rn "MenuDialog\|ActiveMenuDialog"` over the decompiled source returns **zero hits outside
  stock `Fungus/`** — no `TG_*` class references it. The `MenuDialog` prefab ships in
  `resources.assets` only because Fungus ships it. Branching is `storyArc` / `characterAffection`,
  driven by **how the drink is brewed** (see `TG_DebugMode`'s `storyArcChoice` /
  `characterChoiceAffection`), not by picking dialogue lines. A hook on `MenuDialog.AddOption`
  would attach correctly and be silent forever — the exact failure `text-sources.md` warns about.
  Revisit only if the retail build proves otherwise.
- ✅ **Chat log (`TG_ChatLogManager`) — BUILT session 5, untested.** `src/Dialogue/ChatLogPatches.cs`.
  - **The mod supplies an ENTRY CURSOR**, which is a deliberate exception to the standing
    "never build a second cursor" rule. That rule protects against disagreeing with the game's
    cursor; here **there is no cursor to disagree with, for any input device**.
    `UpdateDefaultChatLog` reads `playerActions.Up/Down.IsPressed` continuously and nudges
    `chatLogScroolBar.value` by a float (accelerating after 1 s held). No row index, no
    `Selectable`, no EventSystem focus — a focus watcher would observe nothing.
  - ⚠ **`ChatLog.Set()` DISCARDS its `dialogue` argument.** The line is recovered via
    `GetStoryText(chatLog)`, which resolves `stringId` through the localization dictionary. Reading
    the entry's fields directly yields an empty string for every row.
  - ⚠ **Speaker names must be resolved, not read.** `OnFillItem` shows `????` / localized
    `unknownName` until a character introduces themselves; speaking `characterName` raw would leak
    identities the sighted player does not have. `ChatLogPatches.ResolveSpeaker` mirrors that logic.
  - ⚠ **The decompile lies about two members.** `TG_ProfileData.UnlockedCharacterDataList` and
    `TG_CharacterUnlockedData.IntroducedSelf` are **properties, not fields** — verified by
    reflecting over the real `Assembly-CSharp.dll`. A field-only lookup returns null silently and
    hides every name behind `????`. Always try property AND field.
  - Arrows also pixel-scroll the view (KeyboardNav binds them onto the same joystick set the log
    polls). Harmless: the scroll moves what a sighted onlooker sees, the cursor moves what is
    spoken, and nothing reads the scrollbar back, so they cannot desynchronise.
- ⚠ **No keyboard opener exists.** `KeyboardPlayerActions` defines no Y-equivalent action, and
  `HandlerKeyboard` never opens the log — the only keyboard chat-log binding is **Escape to close
  it** (`TG_KeyboardHotkeyManager:126-129`). The log opens on gamepad **Y**
  (`YButtonPressed`, in `IN_DIALOGUE` / `BREWING` / phone states). **Deferred by the player's call
  (2026-08-09): chat log is gamepad-open only for now.** If added later, forward to the game's own
  `YButtonPressed` (as Enter forwards to `AButtonPressed`) so its state and comic-panel guards are
  reused rather than reimplemented.

## Phase 4 — brewing [recovered]

The other half of the gameplay loop.

- ✅ **Ingredient names + availability — BUILT session 5, untested.** Ingredient buttons are
  ICON-only: they carry no `Text` anywhere, and `TG_Button` (their base) has **no
  `textComponentToMove`**, so every `FocusNarrator.GetLabel` path failed and they announced as
  "unlabeled". Fixed by reading `TG_IngredientButton.value` (an `Ingredients` enum) through
  `TG_DrinkManager.ingredientsLocList`, the game's own **public static** localized name table.
- ✅ **Glass contents + progress — BUILT session 5, untested.** `src/Brewing/BrewingPatches.cs`
  postfixes `AddIngredient` and speaks "Added X. Glass: a, b. 2 of 3."
  - ⚠ **`AddIngredient` returns a STATUS CODE** and its failures are SILENT on screen (the button
    just does not respond): `1` added, `0` glass full, `-1` no such ingredient, `-2` not allowed
    (`base_allow`/`mix_allow` rejected it). We speak the refusal *and* the reason — for `-2` the
    message differs by whether the glass is empty, which tells the player to change the BASE vs the
    MIXER.
  - The glass is re-read in full each time, not just the delta: a sighted player has the three
    slots permanently on screen, and Coffee Talk's puzzles turn on the combination.
- ✅ **Serve result + drink name — BUILT session 5, untested.** Postfixes `GetDrinkNameAndColor`
  (reads the resolved `drinkName` FIELD, not the animated `drinkNameText` label, which may not be
  written yet at postfix time) plus both serve methods and `ResetIngredients`.
- ✅ Auto-focus at ingredient 3 is **announced, not fought** ("Glass full, cursor on Brew").
  `AddIngredient` ends with `brewingButton.Select()` at `glass_value == 3`. Prior art called this
  auto-focus "fighting you"; it only fights a mod that also wants to own focus. We stay read-only.
- ✅ **Live stats + hover PREVIEW — BUILT 2026-08-10, untested.** `src/Brewing/StatsPatches.cs`.
  F9 = query the glass's current stats; the per-ingredient preview is appended to the focus label.
  - ✅ **The flagged unknown RESOLVED IN OUR FAVOUR: `MouseHoverEvent` is NOT mouse-only.**
    `TG_Button` implements `ISelectHandler` and its `OnSelect` body is a bare `MouseHoverEvent()`
    call, so keyboard/gamepad focus already runs the game's own preview computation. The mod only
    READS it. Do not re-investigate; do not build a focus-watcher preview.
  - ⚠ Bars show **RAW stats, not tiers** — `SetIndicatorAllStasBars`' params are *named* `tier*` but
    callers pass raw accumulated ints and it lights one segment per unit. Spoken as counts
    ("sweetness 3 of 5"), denominator measured off `fillBarStatsImage.Length`, omitted if unreadable.
  - ⚠ `AddIngredient` never calls `KillAnimationPreviewStatsDrinkUI` and ends with `Select()`, which
    parks a FRESH preview synchronously — hence the clear on a **POSTFIX** of `AddIngredient`.
  - Stats are a TOGGLE (`BrewInformationClick` swaps the bars against `brewInfoPanel`), so even
    sighted players do not always see them — a query key matches the game's own model.
- ❌ **Tooltips — CLOSED, ALREADY COVERED. Do not build.** `TG_ToolTipManager.ShowToolTip`'s ONLY
  caller is `TG_IngredientButton`, and its key `drinkDescription/<name>Name` resolves to the
  ingredient NAME, which `FocusNarrator` already speaks. A hook would duplicate existing speech.
- ✅ **Latte art — NOT A BLOCKER. Investigated session 5; "announce + skip" was the WRONG plan.**
  `src/Brewing/LatteArtPatches.cs`. The drawing is **cosmetic to the game's logic**:
  `ServeGlassDrink()` and `ServeGlassDrinkLatteArt()` are identical but for a hardcoded
  `latteart:` bool, which flows to `TG_BrewSaveData.LatteArtMade`; every rule that consults it asks
  only `hasLatteArt != latteArt` (`TG_DialogueManager:264`, `:275`). **Booleans — nothing reads the
  fluid sim, the shape, or any quality score.**
  - The accessible path is **entirely native**: the latte art screen has its own
    `serveLatteArtButton`, and pressing it runs `DoCloseLatteArtNServe` ->
    `ServeGlassDrinkLatteArt()` regardless of what was drawn. The mod announces that; it never
    calls the serve itself and fabricates no drawing.
  - ⚠ **A skip would have silently FAILED latte-art drink requests.** The ACT is scored even though
    the ARTWORK is not — do not restate this as "latte art doesn't matter".
  - ⚠ The latte art button only appears for drinks containing MILK (`SetUpLatteArtButton`) or
    predefined drinks with `enableLatteArt`. Its absence is normal, not a fault.

## Phase 5 — full-game systems [new, demo cannot reach these]

Buildable from the decompiled source now, but **untestable until we own the full game.** Build
them, mark them untested, and do not let "untested" block phases 2-4.

- ✅ **Newspaper (`TG_NewspaperManager`) — BUILT session 5, UNTESTABLE HERE.**
  `src/FullGame/NewspaperPatches.cs` postfixes `GenerateNewspaper` and reads the four resolved
  fields off `newspaperObj`. Gated on `activeNewspaperDisplay` (the paper is sometimes generated
  but deliberately hidden). Spoken as ONE announcement — four Speaks would interrupt each other
  and leave only the date audible.
  - ⚠ **Mixed text components**: `mainHeadline`/`bigNews`/`smallNews` are `TextMeshProUGUI`,
    `dateText` is a legacy `UI.Text`. A hook assuming one type reads three quarters of the paper.
  - No navigation needed: `EnterButtonHandler` already handles `READING_NEWSPAPER` on keyboard and
    `TG_ControllerInputManager:1317-1322` on pad. Read-then-dismiss.
- 🔶 **Smartphone — PARTIALLY built session 5. ⚠ THE PHONE IS BLOCKED ON THE DEMO BY DESIGN —
  measured 2026-08-10.** `src/FullGame/SmartPhonePatches.cs`.

  **Read this before any phone work.** `canOpenSmartPhone` is FALSE on the demo, so
  `OpenSmartPhone` takes its ELSE branch: the phone animates in, the state becomes
  `SMART_PHONE`/`PHONE_HOME`, then `screenBlockPanel` goes up, a Fungus block runs (the in-fiction
  "not now" line) and `CloseUnaccesablePhone()` slides it back off ~0.3 s later. **No app panel
  ever activates.** Logs `26-8-10_2-4-50` + `26-8-10_2-21-28`: `[Phone]` fired 5 times, and no
  `TG_SmartPhoneApps.Open` hook has EVER run.

  This supersedes an earlier note (in the source file, now rewritten) claiming "the phone IS
  reachable on the demo, Tab opens it". That was written from a live report of *hearing phone
  words* and never checked against a log. **A hook firing is not a screen working — judge the
  outcome, not the entry.**

  Two bugs it caused, both fixed 2026-08-10: `FocusRecovery` was handing out BREWING buttons under
  a phone label (it now checks `canOpenSmartPhone` live), and the open line named four unreachable
  apps (now "Smartphone unavailable right now").

  ⚠ **THE BREWING-BUTTON BUG WAS ONLY HALF FIXED, AND THE OTHER HALF WOULD HAVE FIRED ON RETAIL.**
  Found 2026-08-10 while investigating item E. The `canOpenSmartPhone` check cured the symptom on
  the DEMO, but the cause was never the blocking — it was recovering on a screen with no
  candidates, and there are two independent ways to reach that:
  - **The whitelist named states the game does not navigate.** `PHONE_SOCMED_ACCOUNT`,
    `PHONE_NEWSPAPER` and `PHONE_NEWSPAPER_DETAIL` were in `NeedsSelection`, but
    `UpButtonPressed`/`DownButtonPressed` route to **only four** phone states (`PHONE_HOME`,
    `PHONE_DRINK`, `PHONE_MUSIC`, `PHONE_SOCMED`). The other three are the Scrollbar panes: analog
    position, no Selectable to move between. Those three are now REMOVED from the whitelist.
  - **`FindEntryControl` scanned the WHOLE SCENE.** The phone draws over a live café whose brewing
    buttons stay active and interactable, so with nothing navigable on the pane the scan returned a
    control from the screen behind. On retail — phone genuinely open, `canOpenSmartPhone` true —
    the guard does not apply and this fires. The search is now scoped to the phone's transform.
    ⚠ Scope is a PREFERENCE with a logged fallback, not a hard filter: the panels are serialized
    inspector references, which say nothing about the transform hierarchy, and a hard filter that
    matched nothing would silently disable phone recovery entirely — trading a wrong-cursor bug for
    a no-cursor bug. If the fallback warning ever appears, the hierarchy assumption is wrong.

  **The lesson: a fix that makes the symptom go away on the build you can run is not a fix.** Ask
  what the FIRST failing step was, and check every other route into it.

  **Panel navigation turned out NOT to be a retail-binary task** — see item E below. Keyboard
  navigation already reaches the phone through `HandlerControllerPress()`, which `KeyboardNav`
  already calls. The CONTENT behind the apps is reachable offline too — see the phone-content
  section below.
  - ✅ Home screen — already covered by `FocusNarrator` (ordinary Buttons + `Select()` at
    `TG_SmartPhoneManager:250,:361`). Nothing needed. ⚠ Unverifiable on the demo per the above.
  - ✅ App opening — one postfix on the BASE `TG_SmartPhoneApps.Open` covers all four apps.
  - ✅ Friend list — IS EventSystem-driven (`ButtonInput` walks `navigation.selectOnUp/Down` and
    calls `SetSelectedGameObject`), so focus is observable. Its text lives on
    `TG_FriendListPrefabUI` (`ProfileNameText`/`ProfileInfoText`, **properties not fields**), which
    the generic scan misses — now labelled via `GetComponentInParent`, since the selected object is
    the Button and the component sits on the row.
  - ✅ **Social media detail pane — BUILT 2026-08-10 as item D.** `src/FullGame/SocialMediaPatches.cs`.
    ⚠ **THIS ENTRY USED TO SAY "BLOCKED TWICE OVER" AND BOTH HALVES WERE WRONG.** Kept rather than
    deleted, because the reasoning error is the reusable part:
    - "Scrollbar-driven, needs an entry cursor" — true of the SCROLL, irrelevant to the CONTENT.
      The pane holds exactly three trivia slots (`SetDetailProfile`'s literal `for i < 3`, bounded
      by `GetTotalAffectionLevel()` which caps at 3). Three bounded strings want one announcement,
      not a cursor. **A scrollbar means "the text may not fit on screen", not "the text is
      unbounded".**
    - "`socialMedia/`, `news/`, `profile/` have ZERO keys" — measured correctly, and about key
      spaces this screen never reads. Names come from `characterNameLocaliztionDictionary`, trivia
      from `GetSocialMediaProfileLocalization(...)` keyed off the character ScriptableObject's own
      `description[]`. **The same adjacent-names trap the next bullet warns about, sprung in the
      very entry that warns about it.**
    - ⬜ **News detail pane is still genuinely open** — it was bucketed with social media here, and
      splitting them is what showed one was buildable. Measure it on its own terms before believing
      either verdict applies.
    - ⚠ **Do not generalise the empty prefix to the newspaper.** The empty one is `news/`; the
      newspaper app reads **`newspaperApp/`**, which has 14 days of title+content present.
      Different key space, opposite conclusion — see the phase below.
  - ✅ **Drink recipes app (`TG_DrinkRecipesApp`) — BUILT session 6, untested.**
    `src/FullGame/DrinkRecipesPatches.cs`. It is NOT scrollbar-driven and was wrongly grouped with
    the panes above: `SetNavigation()` builds an **explicit `Navigation` graph** on each
    `openDescButton` (`selectOnUp`/`selectOnDown` in a loop), so focus is real and observable
    exactly like the friend list — **no mod cursor needed**.
    - Built as a **LABELLER, not a narrator**: the EventSystem focuses `openDescButton` while the
      three `Text`s live on the `TG_DrinkItemUI` ROW above it (same arrangement as
      `TG_FriendListPrefabUI`), so `FocusNarrator.GetRecipeRowLabel` walks the parent chain. A
      second announcer here would talk over the focus line.
    - Focus says name + ingredient triplet + `, expanded`; the DESCRIPTION is spoken from
      `OnButtonClick` instead, because the rows genuinely are collapsible for sighted players too.
      `OnButtonClick` **returns `false` when it CLOSED the row** — that return value is what
      distinguishes opening from closing, and without it a close reads out text just dismissed.
    - `DisplayDrinks` (not the tab buttons) is the announce point for a category: every route —
      tab `onClick`, `ButtonLeft`/`ButtonRight`, and `RefreshList` on open — converges there, and
      it runs regardless of the JOYSTICK gate. Says "Coffee, 9 recipes, 3 still locked".
    - ⚠ `SelectFirstButton()` and `UpdateFunction`'s scroller are gated on
      `ControllerType.JOYSTICK` — the SAME keyboard gate that broke popups. Hence the direct hook
      above rather than reliance on a focus watcher that may observe nothing.
    - ⚠ Locked drinks set name, ingredients AND description all to the single `lockedThingsText`
      string. Detected by comparing against the string the app itself RESOLVED (cached in
      `AfterDisplayDrinks`), so it works in every language; the English substring test is only a
      fallback for a row read before any `DisplayDrinks` was seen. Spoken as "Locked recipe" once
      rather than one identical phrase ~20 times.
    - ⚠ `SwitchToPhone`/`ButtonInput` index `combinedDrinkItemList[0]` unguarded — throws if empty.
      Not touched by the mod (we add no cursor), but do not add a hook that could empty that list.

- ✅ **Achievements — BUILT session 6, AUDITED 2026-08-10 (five defects fixed), untested.**
  `src/FullGame/AchievementPatches.cs`. **The second phase-5 screen read back line by line, and like
  the recipes it was hooked correctly and spoke wrongly.** Two of the five produced actively false
  speech; the audit's own lesson holds, and this screen is the one most likely to FIRE on the demo.
  - ⚠ **The label could have gone missing entirely.** `TG_AchievementIconUI` holds its Button in a
    separate `button` field, so the object the EventSystem focuses need not carry the component —
    but `FocusNarrator`'s component scan is object-LOCAL. Wherever the prefab puts the Button on a
    child, the icon announced "unlabeled" while the composed phrase stayed parked, then leaked onto
    the NEXT control focused. Now via `GetAchievementLabel` through the parent chain, exactly as
    friend rows and recipe rows already were. **Two screens had already needed this fix; the third
    was written without it.**
  - ⚠ **`SetSelectedData` fires at least TWICE per focus move.** `TG_AchievementIconUI.MouseHoverEvent`
    calls `button.OnSelect(null)` — whose body is a bare `MouseHoverEvent()` — and then
    `button.Select()`, so the override re-enters itself. Harmless here (the parked phrase is
    idempotent, last write wins) but it falsifies the "sole convergence point, fires once" reasoning
    the file was built on. **Same shape as `RefreshList` → `DisplayDrinks` and `SayDialog.Say`:
    check the CALLERS for re-entry, not only that they converge.** Recorded so nothing later adds a
    counter or a direct Speak here.
  - ⚠ **No position was spoken, on a 72-cell grid where every edge WRAPS.** `Init` builds explicit
    four-way Navigation in which the last icon's `selectOnRight` is icon 0 and `selectOnDown` past
    the bottom row returns to `i % GRIDHORIZONTALLENGTH`. A sighted player sees the cursor jump the
    screen; the wrap was completely inaudible. Now ", 14 of 72", indexed off the manager's own
    `achievementIconUIs` list — **not** `indexButton`, which is filled by `SetMenuIndex` and nothing
    in `Init` ever calls it, so it is 0 for every icon.
  - ⚠ **`Init` is not the moment the screen opens — it ran ~1 s early, and interrupted.**
    `OpenAchievement` calls `Init()` FIRST, then fades a cover panel 0.5 s, activates the object,
    fades back 0.5 s, and only then calls `SelectFirstButton()`. The entry line landed in the middle
    of the extras menu with `interrupt:true`, able to cut the line still being read. Now `Init` only
    ARMS an `EntryWatcher` polled from `OnUpdate` (same shape as `NameScreenWatcher`, and for the
    same reason: the screen becomes live inside DOTween callbacks with no method to postfix), which
    speaks on `activeInHierarchy` and does **not** interrupt. ⚠ `Init` also RE-RUNS on every entry —
    `initialized` guards only the prefab instantiation, not the per-icon loop or the progress text.
  - ⚠ **"Hidden" was inferred from the MASK alone, which mislabels an unlocked achievement.** The
    game masks on `hiddenAchievement && !unclocked`, so `????` implies locked — but if a *visible*
    achievement's name legitimately resolves to `????` (a missing localization term, which is
    exactly how `DirectLocalization` surfaces an absent key) the row was announced "Hidden
    achievement, locked": both halves wrong, and the second half wrong about the only question this
    screen answers. Now reads the STATE first; a `????` surviving to the normal path says
    "name unavailable" — an audible gap rather than a run of question marks.
  - ✅ **New: leaving the screen clears both channels.** A postfix on `BackToExtrasMenu` drops the
    armed entry line and any parked label. An un-cleared parked label becomes the caption of the
    next control focused — the failure `coffee-talk-dedup-on-identity` warns about.
  - Verified by reflecting the shipped `Assembly-CSharp.dll`: all three hook targets exist with the
    expected signatures, and all five fields read are genuine FIELDS (no property trap). **Still
    never executed.**
  `TG_AchievementMenuManager.SetSelectedData` is a single convergence point filling
  name/description/howToUnlock, driven by `hoverAction` from `MouseHoverEvent`, which fires on
  KEYBOARD focus via `ISelectHandler` (verified 2026-08-10).
  - ⚠ **These two hooks may actually FIRE on the demo**, unlike the rest of phase 5: achievements
    hang off the MAIN MENU's extras screen, not off the story. Worth a look in the next playtest.
  - Icons carry no `Text` — same icon-button shape as ingredients — so this **parks a phrase in
    `PendingAchievement` and `FocusNarrator` uses it as the LABEL** (the `PendingStats` channel
    shape). A self-speaking hook would be cut off by FocusNarrator's "unlabeled" line a frame later.
  - Reads the three **resolved panel components**, never `TG_AchievementData`, so the `????`
    hidden-achievement masking is inherited rather than re-implemented. Spoken as "Hidden
    achievement, locked" — a screen reader voices a literal `????` as noise or as nothing.
  - Locked/unlocked is stated EXPLICITLY: it is carried only by the icon SPRITE, so it is otherwise
    inaudible, and it is the whole question this screen answers. How-to-unlock is spoken only while
    still locked. `Init` announces the "N of 72" progress once on entry.
  - ⚠ The earned flag is spelled **`unclocked`** (a typo in the shipped assembly, confirmed by
    reflection). Do not "fix" it into a silent null read.
- ✅ **Save/load calendar (`TG_CalendarUIManager`) — BUILT session 6, AUDITED 2026-08-10 (four
  defects fixed), untested.** `src/FullGame/CalendarPatches.cs`. Postfixes `SetSelectedData` (the
  focused day) and `SetLastPlayedData` (the Continue affordance).
  **The third phase-5 screen read back line by line, and the third to be hooked correctly and speak
  wrongly.** Unlike the first two, TWO of its four defects ran on every single open of the screen
  rather than on an edge case. The parked-label channel — the thing this screen was expected to get
  wrong — was the one part that was already right.
  - ⚠ **THE OBVIOUS CLASS IS THE WRONG ONE.** `TG_CalendarContent` looks like the day cell and is
    **mouse-only** — it implements ONLY `IPointerEnterHandler`/`IPointerExitHandler`, so a hook
    there never fires on keyboard. The load grid is `TG_CalendarLoadUI : TG_Button` (hence
    `ISelectHandler`, hence keyboard focus) with a `hoverAction`, wired in `TG_SaveMenuManager`.
    The two classes are one letter apart in a file listing.
  - ⚠ **CARRIED THE ACHIEVEMENTS BUG TOO — fixed 2026-08-10 without waiting for its own audit.**
    `TG_CalendarLoadUI` extends `TG_Button` and inherits its `button` field, so the focused object
    need not carry the component, and `FocusNarrator`'s scan for it was object-LOCAL. Now routed
    through the parent chain (`GetCalendarDayLabel`) like the achievement, friend and recipe rows.
    **Four screens now use the parked-label channel and three of them were misrouted the same way** —
    if a fifth is added, route it through the parent chain from the start.
  - Focus is the game's own: `TG_SaveMenuManager` wires explicit four-way `Navigation` and **skips
    disabled cells** (walking by 7 — a week per row) so the player never lands on a day with no
    save. Mod adds no cursor. Labeller, via `PendingDay`.
  - ⚠ **"No save" WAS THE WRONG WORDS FOR THE WRONG CONDITION — the line above used to say so.**
    `SetSelectedData`'s ELSE branch blanks the fields when `day >= TG_Static.dailyDataList.Count`:
    a day **past the end of the story**, i.e. a cell drawn to fill out the last week. Days that
    merely have no save never reach it — those are `button.interactable = CheckUnlockedDay(i + 1)`,
    and the navigation graph walks past them, so they are normally unreachable and already say
    ", unavailable". The mod was announcing a routine empty cell with a much more alarming claim
    than the truth. Now "no story". **The branch still must speak** — silence there is
    indistinguishable from a broken hook.
  - ⚠ **The day number was OFF BY ONE, and it was the mod's only spoken number.** `day` is a
    ZERO-BASED index; every number the game DISPLAYS is one higher (`GetDayNumberFormatLocalization(day + 1)`,
    `CheckUnlockedDay(i + 1)`). The fallback spoke the raw index, so the mod disagreed with every
    label on the screen. **Read what the game displays, not what it indexes** — the same class of
    error as the recipes tab naming a base by its enum instead of its on-screen name.
  - ⚠ **`lastPlayedText` IS NEVER POPULATED — nothing in the decompiled game assigns it.**
    `InitLastPlayInformation` writes only `dayNumberText`, `dayText` and `clockText`; the fourth is
    scene-authored chrome. It was read FIRST and allowed to satisfy the "is there anything to say"
    check, so a profile with no quicksave (whose ELSE branch blanks the other three and leaves this
    one set) would announce a bare caption with no data behind it. The check now keys only on the
    three fields the game actually writes. **A field existing in the decompile is not evidence
    anything ever fills it — grep for the writer.**
  - ⚠ **The continue-summary TALKED OVER the focused day, on every open.** `RefreshSlot` calls
    `SetLastPlayedData()` from inside `OpenSaveMenu`, which then waits 0.1 s realtime and a 0.6 s
    fade before `SelectLastPlayedCalendar()` gives the grid focus. Speaking at fill time with
    `interrupt:true` meant the summary fired ~0.7 s early and was then cut off by the day cell — or
    cut the day cell off, depending on fade timing. **This file's own newspaper rationale (one
    control, one utterance) was not being applied to itself.** Now parked in an `EntryWatcher`
    polled from `OnUpdate` and spoken on `activeInHierarchy` with `interrupt:false`, the same shape
    as `AchievementPatches.EntryWatcher` and for the same reason.
  - ✅ **New: both exits clear both channels.** Postfixes on `TG_SaveMenuManager.BackToMainMenu`
    AND `BackToPauseInGameMenu` — `TG_CalendarUIManager.Initialize` picks between them on
    `TG_Static.currentScene`, so hooking only the main-menu one would leave a parked day label
    armed on the in-game pause route, to become the caption of the next control focused.
- ✅ **Gallery + comics — BUILT session 6, untested.** `src/FullGame/GalleryPatches.cs`, four
  postfixes on `SetLargeImage`/`SetBiggestImage` of both managers.
  - ⚠ **THE GALLERY IS NOT CHROME-ONLY — the survey below was WRONG about it.** `TG_GalleryDisplay`
    carries a per-picture `description` string, shown in `biggestGalleryDescriptionText` on the
    full-screen view. Real authored prose about the artwork; now spoken. The COMICS are
    chrome + title + panel count only, as surveyed.
  - ⚠ **TWO MORE DECOY CLASSES** (third and fourth in this codebase, after `TG_CalendarContent`):
    `TG_BigPictureGallery` is a single `Image` field, not a screen. `TG_GalleryItem` looks like the
    item model and has exactly the members you'd want (`isUnlocked`, `key`) — but the manager's list
    is **`TG_GalleryDisplay`**, a different type, and reflection confirms `TG_GalleryDisplay.key`
    and `TG_ComicDisplayUI.isUnlocked` **DO NOT EXIST**. A plausible class name is not evidence
    here; verify against the shipped assembly every time.
  - Hooked the two Set*Image METHODS, not the `CurrentIdx` property setter that calls them: same
    coverage, but they receive the display object (where the description lives) and they run after
    the setter has CLAMPED the wrapped index.
  - Locked state is spoken because it is carried only by the sprite + image anchoring — no text on
    screen says it. Dedup on the announcement text: several paths re-set the same item.
  - The artwork itself stays undescribed and the announcements SAY SO ("Artwork not described")
    rather than trailing off into silence.
- ✅ **Ending epilogues — BUILT session 6, untested.** `src/FullGame/EndingPatches.cs`.
  ⚠ **This screen was recorded as "chrome only" TWICE and both times was WRONG.** The `credit/`
  namespace (**SINGULAR**) holds **27 keys** — `credit/luaBaileysGood1`, `credit/hydeGalaNormal2`,
  `credit/rachelHendryNormal1`: **per-character epilogues that vary by story-arc outcome.** That is
  the payoff for a whole playthrough, and it was the most content-bearing thing left in phase 5.
  - ⚠ **How BOTH surveys missed it.** (1) Term-counting classes found nothing. (2) Checking item
    types found only `Text`/`TMP` COMPONENTS — render targets, not authored strings. Neither
    grepped for a **WRITE to `.text`**. There is one, in `DOImage1Animation`.
    **A guessed key prefix returning zero is not evidence. Grep for the write, then read the prefix
    off the code that performs it** — my earlier sweep counted `credits/` (plural, zero).
  - ⚠ **Hooked `GetDialogueEndingCutscene`, NOT the writer.** The writes are inside
    `DOImage1Animation`, an **ITERATOR** — a postfix there fires before the body runs and sees no
    text (same trap as `SayDialog.Say` vs `DoSay`). `GetDialogueEndingCutscene` is a plain method
    returning the two keys, called on the line immediately before they are used, with the arc
    outcome already resolved.
  - Keys are re-resolved through the game's localizer rather than read off the components, which
    are not written until the next two lines. Spoken as ONE `TextType.Narrator` announcement (so
    the repeat key stores it — the text fades on a timer).
- ⬜ **Rolling credits — chrome only, genuinely.** `TG_CreditsEndGameManager` /
  `TG_CreditPanelUI` only animate colour, position and alpha; the roll's strings are authored in
  the Unity scene with no `.text` write to hook (verified by the same grep that FOUND the epilogue
  writer — so the negative here has a positive control).
  These were bucketed with the calendar as one to-do. Measuring (2026-08-10) split the bucket:
  they are **VISUAL surfaces** whose content is images, not text. The only localization terms
  `TG_CreditsEndGameManager` or `TG_EndingCutSceneManager` reference are `generalUI/page` and
  `generalUI/artByGallery` — chrome, not content.
  ⚠ The same survey put GALLERY in this group and was **wrong**: term-counting a class misses data
  carried on its item objects, and the gallery's descriptions live on `TG_GalleryDisplay.description`
  (a plain field, no localization term to count). Before deprioritising a screen as "visual", check
  its ITEM type's fields, not just the manager's string literals. Endings/credits were re-checked
  on that basis and do hold up as chrome-only.
  - ⚠ The `calendar/`, `gallery/`, `comic/`, `ending/`, `credits/` namespace counts are all ZERO,
    but that is NOT the evidence — those prefixes were my guess. The positive control (`generalUI/`
    = 75, `achievements/` = 72 in the same sweep) proves the method works; the class-level term
    extraction above is what actually establishes the claim.
  - What is buildable here is **navigation chrome** (page N of M, "art by"), not content. Worth
    doing eventually so a player can move through these screens and knows what they are; it does
    not make the pictures accessible, and it should not be described as if it does.

> ⚠ **THE DEMO SHIPS THE FULL RETAIL DATA SET — measured 2026-08-10, do not re-defer on a guess.**
> `resources.assets` contains **72 `achievements/` keys** (including `ACH_FINISH_GALA_HYDE_ARC` and
> endless-mode entries the demo can never award), **~31 drinks in `drinkList/`**, 40 `newspaper/`,
> 49 `endlessModeDialog/`. Build settings even list `DailyCutScene` and `EndlessModeScene`.
> "The demo cannot PLAY it" and "the data is not THERE" are different claims; only the second is a
> reason to defer, and it holds for `socialMedia/`/`news/`/`profile/` alone.
> ⚠ Game data folder is `CoffeeTalk_Data` — **no space**.

## Phase 6 — the full game

- ⬜ Re-verify every hook against the retail assembly (names may differ from the demo).
- ⬜ Daily cutscenes — same `TG_CutsceneManager` machinery as the opening, likely already covered
  by the base-class hook.
- 🔶 Endless mode (`TG_EndlessMode*`) — **likely ALREADY COVERED for free**:
  `TG_EndlessModeDialogManager.SetSayDialogText:36` calls `obj.Say(...)`, which the existing
  `SayDialog.Say` hook catches. Verify rather than rebuild.
- ⬜ Save/load across real profiles.

## Phone content from available data (planned 2026-08-10, not started)

> **Deliberately unnumbered.** Phases 0-6 above are a HISTORY, ordered by when each was built. This
> is not the next item in that sequence — it is new work that routes AROUND the blocked phone UI,
> and numbering it would imply a priority ordering the number cannot carry. It was briefly filed as
> "Phase 3b" and physically sat inside Phase 5's bullet list, which orphaned the achievements entry
> and made the numbering unreadable. New work goes in named sections from here on; see **Next up**
> at the top of this file for what is actually next.

The phone's UI is blocked on the demo, but each app's underlying DATA is reachable offline. So
build a content layer that does not depend on the panels opening, plus a thin adapter for when they
do. The content layer is most of the work and transfers to retail unchanged. Same shape as
`ChatLogPatches`: a cursor over the DATA, never over the analog scroll position.

- ✅ **A. Newspaper reader — BUILT 2026-08-10. `src/FullGame/NewspaperAppPatches.cs`. UNTESTED
  AND UNREACHABLE ON THE DEMO BY DELIBERATE CHOICE.**
  Postfixes `TG_NewspaperApp.SetNewsOnApp` (the single convergence point: `Open()` and both
  arrow-button handlers all call it) plus `Open` for the subscribe-nag branch and
  `TG_SmartPhoneApps.Close` filtered to this app. Paragraph cursor stepped from `OnUpdate`.
  - ⚠ **The player was offered a mod hotkey that would have made this testable on the demo today,
    and chose "hook the real app only"** — keeping the mod's "narrate the real screen, invent
    nothing" shape. Legitimate, and implemented faithfully, but the consequence is that **nothing
    in this file has ever executed**: the phone is blocked (`canOpenSmartPhone == false`) AND this
    app's `Open` shows `pleaseSubscribeObject` on an expo build. Two independent gates. Do not
    let it drift to "working" without a retail run.
  - **Text is read back off the UI components, NOT re-derived from the keys.** By the time the
    postfix runs the app has already resolved `newspaperApp/content{day+1}` and applied its own
    empty-string fallback to day 1. Re-doing that lookup would mean a second copy of the day-index
    and fallback rules — two places to drift. Reading `newsDetailContentUI` inherits all of it,
    in the player's language, for free.
  - ⚠ **`FungusText.ExtractWords` CANNOT be applied to the whole body**: it ends in `Collapse()`,
    which flattens newlines into spaces and would yield ONE paragraph with nothing to step
    through. Split on blank lines FIRST, clean each paragraph after. Verified against the real
    bytes: day 2 → 21 paragraphs, day 3 → 5, day 10 → 38, and day 2's four-line song verse
    correctly stays a single unit because only a BLANK line ends a paragraph.
  - ⚠ **Mixed text components, as on the physical paper**: title/body are `TextMeshProUGUI` (on
    `TG_NewsDetailContentUI`), the date is a legacy `UI.Text` (on the app). Both overloads needed.
  - Position counter is spoken only on landmarks (first, last, every 10th, or ≥200 chars). The
    real articles are largely quoted dialogue — day 10's shortest paragraphs are `"What?"` and
    `"Not this again."` — and "2 of 38." on those is more bookkeeping than story.
  - ⚠ Do not confuse key spaces: `news/` has ZERO keys in the demo, `newspaperApp/` has all 14
    days. Adjacent names, opposite conclusions.
  - ⚠ Do not confuse SCREENS either: this is the phone ARCHIVE. `NewspaperPatches.cs` is the
    physical morning paper (`TG_NewspaperManager`), a different source and different keys.
- ✅ **B. Recipes — AUDITED 2026-08-10. "Mostly already covered" was right about COVERAGE and wrong
  about CORRECTNESS: the hooks were all on the right methods, and four of them spoke wrongly.**
  No new screen was needed; every fix is in `DrinkRecipesPatches.cs` (+ one in `Main.cs`).
  - ⚠ **The tab announcement named two tabs after the wrong thing.** The tabs are the five BASES,
    but the app shows `greentea` as **Matcha** and `chocolate` as **Cocoa** (its own
    `MATCHA_CONST`/`COCOA_CONST`, `matchaIcon`/`cocoaIcon`). Speaking the localized INGREDIENT name
    announced "Green Tea" for a tab labelled Matcha — sending the player to look for a tab that is
    not on screen. Now via `TabName`. **Mirror the screen's vocabulary, not the enum's**; this is the
    same class of error as speaking raw `????` or a bare object name.
  - ⚠ **Opening the app spoke the tab line TWICE.** `RefreshList` calls `DisplayDrinks` directly and
    *then* invokes the tab button's `onClick`, whose listener calls it again for the same category.
    Deduped on the rendered LINE (not the enum), so a genuine repopulate that CHANGED the counts
    still speaks. Cleared on `Close` — an uncleared dedup turns a duplicate into permanent silence.
  - ⚠ **`lockedThingsText` could be blanked mid-session.** It is a real public field, but is only
    assigned in `InitLocalization`; caching an empty read would have silently disabled locked-row
    detection for the rest of the run, after which every locked row reads the placeholder sentence
    aloud. Now only a non-empty value overwrites the cache.
  - ⚠ **`drinkDescriptionObject.activeSelf` IS NOT READABLE from the expand hook.** `OnButtonClick`
    only *starts* `DoRefreshContentSizeFitter`, a coroutine that waits two end-of-frames before
    leaving the object active — so at postfix time an opening row still reads as closed. The
    description STRING is safe (written at list-build time); only the visibility is not. Recorded in
    the file so nothing later tests that flag there. `DescribeRecipeRow`'s ", expanded" runs a frame
    later from the focus watcher and is unaffected.
  - ⚠ **`ReportDoublePatches` now needs an exemption for `TG_SmartPhoneApps.Close`.** It is a shared
    base method that the newspaper and recipes readers both postfix ON PURPOSE, each filtering on
    `__instance is <their app>`. Without the exemption it printed a red DOUBLE-PATCHED error at every
    startup — and a diagnostic that cries wolf teaches the player to ignore the real ones.
  - Verified by reflecting the built DLL: all three hooks bind to real, correctly-named targets.
    **Still never executed** — the phone is blocked on the demo, so this remains a retail-run item.
- ✅ **C. Music metadata — BUILT 2026-08-10, untested.** `src/FullGame/MusicAppPatches.cs`.
  Announces the track on play ("Now playing, X, by Y, from Z") and labels playlist rows through
  `FocusNarrator.GetSongRowLabel`.
  - ⚠ **"Target the shared base `TG_MusicAppGeneral`" was WRONG, and wrong in the most dangerous
    direction.** Every method on that base is an EMPTY VIRTUAL — verified by reflection over the
    shipped DLL: `PlaylistSongButtonClick`, `NextButtonClick`, `PreviousButtonClick` and
    `ButtonInput` are all `virtual=True, IL bytes=1` (a bare `ret`). Harmony patching a virtual
    does NOT intercept calls dispatched to an override, so a postfix there would ATTACH, be
    reported **green by `VerifyExpectedPatches`**, and never once fire. That is precisely the
    "a silent no-op looks exactly like a working build" failure this project's rule 1 exists to
    prevent — and the plan's own advice would have walked into it.
    **Both concrete classes are hooked instead.** The warning's REASONING was right (testing only
    against the demo class gives false confidence about retail); only its remedy was wrong.
  - ⚠ The demo's override spells its second parameter **`pauseSog`**, not `pauseSong` — a typo in
    the game. Harmless here (the hooks bind `__instance` and `index` only), but any patch naming
    that parameter would fail to bind on one class and not the other.
  - `SongName`/`ArtistName`/`AlbumName` are **properties with NO backing field visible to
    reflection** (confirmed: `property=True field=False` for all three), so `AccessTools.Field`
    returns null silently. Property-then-field `Read<T>`, as with the friend list.
  - Rows are a LABELLER via the parent chain: `TG_PlaylistSongUI` implements `ISelectHandler` and
    holds `SongNameText`/`ArtistNameText` as properties, while the EventSystem focuses the child
    `PlaylistSongButton`. Composed explicitly because a component scan finds the two Texts in
    hierarchy order — artist before song as often as not.
  - `_nowPlaying` deliberately SURVIVES `Close` (the café keeps playing the track); only the dedup
    resets, so re-opening the app announces the current song again.
- ✅ **D. Social media — BUILT 2026-08-10, untested.** `src/FullGame/SocialMediaPatches.cs`
  postfixes `TG_SocialMediaDetailProfileUI.SetDetailProfile` — the convergence point for both the
  friend-row click and `RefreshDetailProfile` on re-open. Still reads
  `profileData.UnlockedCharacterDataList`, which is PLAYER PROGRESS rather than shipped content,
  so it is near-empty on the demo's single day. `DescribeFriendEntry` already covered the LIST;
  this adds the DETAIL pane.
  - ⚠ **"Blocked twice over" did not survive the source, on either count.**
    1. The Scrollbar is not in the way of the CONTENT. The pane holds exactly **three** trivia
       slots (`SetDetailProfile`'s literal `for i < 3`), bounded by `GetTotalAffectionLevel()`
       which caps at 3. Three bounded strings need one announcement, not an entry cursor — the
       newspaper's one-utterance rule applied to a much smaller body.
    2. The measured-empty prefixes were `socialMedia/`, `news/` and `profile/`. **This screen
       reads none of them**: names come from `characterNameLocaliztionDictionary`, trivia from
       `localizer.GetSocialMediaProfileLocalization(...)` keyed off the character
       ScriptableObject's own `description[]`. Whether those resolve is a SEPARATE question from
       the three prefixes that were measured — the same adjacent-names trap as `news/` versus
       `newspaperApp/`, and it produced the opposite conclusion for the same reason.
  - ⚠ **A NARRATOR, not a labeller — deliberately the opposite of every other row screen.**
    `FriendListButtonClik` calls `SetSelectedGameObject(null)` (`TG_SocialMediaApp:255`) and the
    pane contains no focusable item, so there is NO focus event to attach a parked label to. If
    this postfix stays quiet, nothing speaks at all.
  - ⚠ Speaks with `interrupt:false`: `DoRefreshDetailContent` toggles the pane active/inactive
    three times across three frames before settling, and the friend row the player just activated
    may still be being read.
  - **Locked trivia is detected by `FontStyles.Italic`**, the game's own signal — `SetDetailProfile`
    sets Italic for locked and resets to Normal on the same line that writes the real text. Works
    in every language, unlike matching the English placeholder string. Locked slots are COUNTED
    ("2 more things to learn") rather than read aloud as the same sentence three times.
  - `ProfileNameText`/`TriviaText` are properties with no reflectable field (confirmed), so the
    property-then-field helper is load-bearing here too.
- ✅ **E. Adapter — INVESTIGATED 2026-08-10, and there was NO ADAPTER TO WRITE.** The one-line
  description ("drive the same readers from inside the real panels") described work that the
  existing code already does. Traced end to end:
  - **Navigation already reaches the phone on keyboard.**
    `TG_ControllerInputManager.UpButtonPressed`/`DownButtonPressed` (+ their Hold twins) route to
    `musicScreenPanel` / `recipesDrinkScreenpanel` / `socialMediaScreenPanel`.`ButtonInput(...)`,
    and they are dispatched from **`HandlerControllerPress()`** — the exact method `KeyboardNav`
    already invokes every frame in keyboard mode (`KeyboardNav.cs:120`). Arrow keys have therefore
    always travelled the phone's own navigation path.
  - **Back-out already works without the mod.** `TG_KeyboardHotkeyManager.EscapeHandler` handles
    all **eight** `PHONE_*` states natively and calls `BackButtonFunctionDelegate()`.
  - **The readers hook the game's own methods** (`SetNewsOnApp`, `DisplayDrinks`,
    `PlaylistSongButtonClick`, `SetDetailProfile`), which fire from inside the real panels however
    the panel was reached. They were never waiting on a bridge — only on the panels opening, which
    is a DATA condition (`canOpenSmartPhone`), not a code one.

  **What the investigation DID find is a real bug, now fixed** — see the `FocusRecovery` entry
  below. Worth noting that it was found by asking "what does this adapter actually have to do?"
  rather than by writing the adapter: **tracing the requirement was the deliverable.**

**Superseded 2026-08-10 — C and D are now built, and the caution behind this line still stands.**
It read "stop after A and B; C and D grow untested surface, which is what has been distorting the
version number." That was the right instinct and it was overtaken by the request to finish the
remaining code items. The surface it warned about is now larger, not smaller: **A, C and D have
never executed a single line in the game, and B has not either.**

**E (adapter) is deliberately NOT built for exactly this reason.** It is the one remaining item
that could be written, and it is glue that runs only when `canOpenSmartPhone == true` — so on the
demo every line of it is unreachable, unrunnable and unverifiable. A fourth consecutive
compile-only feature buys nothing; a retail run buys all four at once. **Build E when the panels
can open, not before.**

## Deliberately deferred

- **Latte art as a DRAWING** — the mouse-gesture fluid sim itself stays inaccessible. This is now a
  cosmetic gap only: the scored outcome is fully reachable (see Phase 4), so nothing in the game is
  locked behind it. Revisit only if a player wants the picture for its own sake.
- Live brewing stats on every focus move — probably a query key instead; see Phase 4.
- Anything requiring the retail build, until we have it.
