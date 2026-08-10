# Coffee Talk — every source of on-screen text

Survey done 2026-08-09 (session 3), from the decompiled assembly. Scope is the **full game**, not
just the demo, because the demo is a staging ground for the whole thing.

The point of this document: Coffee Talk has **at least four independent text systems** that share
no common sink. A hook on one is invisible to the others. Two live runs were spent on hooks that
were correctly applied and correctly silent because they watched the wrong pipe.

## The systems

### 1. Fungus — main story dialogue
- Sink: `Fungus.SayDialog.Say(text, ...)`. Callers: `Say.cs:191`, `ConversationManager.cs:338`,
  `TG_EndlessModeDialogManager.cs:36`.
- Speaker name: `SayDialog.SetCharacterName` (resolved; preserves `????`).
- ⚠ `TG_SayDialog.SetDialogPosition` writes `saydialog.StoryText.text` **directly**, bypassing
  `Say`. Checked: the only caller (`Say.cs:189`) passes `null`, clearing the field immediately
  before `Say` fills it. So it is a positioning helper, not a second text path. **Hooking `Say` is
  correct.** Do not re-investigate this without new evidence.
- Status: **HOOKED** (`SayPatches`), never yet observed firing — no run has reached story dialogue.

### 2. TG_CutsceneManager — opening / daily cutscenes
**Completely separate from Fungus.** This is what read as "several lines the mod did not read".
- Text source: `TG_Static.localizer.OpeningCutsceneLocalization()` -> `cutsceneTextList`.
- Line selected into `currentWholeText` by `SetDialogueText()`.
- Rendered ONE GLYPH PER FRAME by the `DisplayText()` coroutine (`TG_CutsceneManager:151-163`).
- ⚠ `SetDialogueText` is **virtual with an EMPTY base body**; the real implementation is
  `TG_OpeningCutSceneManager`'s override. Patching only the base attaches to a method the opening
  cutscene never runs — another silent-but-applied hook.
- ⚠ Do NOT hook `DisplayText`: it would re-speak the line on every character.
- Second text stream: `creditText` via `GetCreditOpeningText` — per-panel credit line, separate
  `Text` field, not covered by the line hook.
- Only subclass in the build is `TG_OpeningCutSceneManager`, but `DoCutscene` ends by switching to
  `"DailyCutScene"`, so the same machinery serves the daily cutscenes in the full game.
- Status: **HOOKED** (`CutscenePatches`), UNTESTED.

### 3. Unity EventSystem UI — menus
Covered by the existing `FocusNarrator` + `MenuPatches` + `OptionPatches`. Working live.
- ⚠ Unlabeled controls now announce "…, unlabeled" instead of passing the bare object name off as
  a caption. The name field reading as the single word "Input" sounded like a terse real label
  rather than a mod gap, which is why it went unreported as a bug.

### 4. Direct `.text` writers — everything else
~50 classes assign `.text` outside the above. These need per-system hooks. Prioritized:

| System | Class | In demo? | Status |
|---|---|---|---|
| Name entry | `TG_NameKeys` | yes | **HOOKED**, untested |
| Press-any-key prompts | `TG_PressAny*BlinkTextUI` | yes | **HOOKED**, untested |
| Confirm/load popups | `TG_PopUpUI`, `TG_PopUpManager` | yes | TODO — see below |
| Dialogue choices | `Fungus.MenuDialog.AddOption` | **NO — does not exist** | ❌ CLOSED, see below |
| Brewing | `TG_DrinkManager` | yes | TODO |
| Chat log | `TG_ChatLogManager` | yes | **HOOKED**, untested |
| Tooltips | `TG_ToolTipManager` | yes | TODO |
| Newspaper | `TG_NewspaperManager` | full game | TODO |
| Smartphone (all apps) | `TG_SmartPhoneManager` + `TG_*App` | full game | TODO, known-hard |
| Achievements | `TG_AchievementMenuManager` | full game | TODO |
| Calendar / save-load | `TG_Calendar*`, `TG_SaveLoad*` | full game | partly via OptionPatches |
| Gallery / comics | `TG_GalleryManager`, `TG_ComicMenuManager` | full game | TODO |
| Endings / credits | `TG_EndingItem`, `TG_CreditsEndGameManager` | full game | TODO |

## Popups are the next real gap

`TG_PopUpManager` drives BOTH confirmation and load popups. Text enters via
`TG_PopUpUI.SetPopUpTitleText` / `SetTextTerm`, and `TG_PopUpLoadUI` adds `SetInfoText`.

This matters beyond narration: the name screen's confirm step opens one
(`TG_NameKeys:183-186`), and `SelectButtonPopUpConfirmation` only calls `Select()` when the
controller type is **JOYSTICK** — so on keyboard the popup buttons may never take EventSystem
focus, and FocusNarrator would see nothing. Hook `SetPopUpTitleText` and `SetInfoText` directly
rather than relying on focus.

## Dialogue choices do not exist in this game (CLOSED 2026-08-09)

**Do not build a `MenuDialog.AddOption` hook. It would attach cleanly and never fire.**

Evidence:
- `grep -rn "MenuDialog\|ActiveMenuDialog"` over the whole decompiled tree returns hits **only
  inside stock `Fungus/`**. No `TG_*` class mentions it. Fungus's own `Menu`/`MenuDialog`/
  `MenuTimer`/`MenuShuffle` are unreferenced engine baggage.
- The `MenuDialog` prefab does appear in `resources.assets`, but only because
  `MenuDialog.GetMenuDialog()` lazily `Resources.Load`s it — presence of the prefab is not
  evidence of use.
- Coffee Talk's branching is `storyArc` + `characterAffection`, set by **how the player brews the
  drink**. `TG_DebugMode` exposes exactly these (`storyArcChoice`, `storyArcChoiceValue`,
  `characterChoice`, `characterChoiceAffection`) and nothing resembling a dialogue option.

This is the canonical example of the warning at the top of this file: a hook can be correctly
applied and correctly silent because the system it watches is not the one in use. The accessibility
work for "player influences the story" therefore belongs to **brewing narration (Phase 4)**, not to
a choice menu.

## Localizer inventory

`TG_Static.localizer` exposes ~26 text feeds; each is a candidate system. Notables not yet
covered: `DailyCutsceneLocalization`, `RequestDrinkDialogueLocalization`,
`FreeBrewResponse*DialogueLocalization`, `DrinkDescriptionLocalization`, `BigNewsLocalization`,
`DailyNewsLocalization`, `SmallNewsLocalization`, `CreditsLocalization`,
`Opening/ClosingEndlessModeDialogueLocalization`, `GetSocialMedia*Localization`.

`DirectLocalization(tag)` (50 call sites) is the generic term lookup — it resolves an I2.Loc key,
so it is a good way to get correctly translated strings for mod-authored announcements.
