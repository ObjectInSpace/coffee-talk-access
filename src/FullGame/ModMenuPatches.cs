using System;
using System.Reflection;
using CoffeeTalkAccess.Menus;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoffeeTalkAccess.FullGame
{
    /// <summary>
    /// The Steam Workshop mod manager (retail only), reached with Tab - see MainMenuHotkeys.
    ///
    /// Reported live as "once I enter the mod menu, it doesn't read". The log showed it was not
    /// silent - it spoke twice and stopped:
    ///
    ///   [Hotkey] Tab -> mod menu
    ///   [Focus] supplied missing keyboard selection on MOD_MENU: CloseModButton
    ///   [Focus] Close Mod Button, unlabeled
    ///   [Focus] REMOVE ALL MOD
    ///
    /// Those ARE the only two controls, because no Workshop mods are installed. So the screen was
    /// working; what was missing is any statement of WHAT the screen is and that its list is empty.
    /// A player who hears an unlabeled button and then one stray caption has no way to tell an empty
    /// list from a broken reader - the same "silence is indistinguishable from a dead key" failure
    /// this project keeps designing out, in a new place.
    ///
    /// ⚠ THIS SCREEN IS JOYSTICK-GATED IN THREE PLACES, not the usual one:
    ///   1. TG_ModManagerUI:121 - SetFirstSelectedButton() on open (the familiar gated-Select bug,
    ///      already covered by FocusRecovery's MOD_MENU entry).
    ///   2. TG_ModManagerUI:190 - ControllerHandle, the ENTIRE input handler, so B-to-close and the
    ///      LB/RB switch between the "all mods" and "active mods" tabs never run on keyboard.
    ///   3. TG_ModContentListPanelUI:414 - SetFirstSelectedButton no-ops when the list is empty
    ///      (`if (maxUILength > 0)`), which is why recovery fell through to the close button.
    /// ChangeUI:217 even has an explicit KEYBOARD branch that hides the controller hint panel, so
    /// keyboard players were a known case that simply got less.
    /// </summary>
    [HarmonyPatch]
    public static class ModMenuPatches
    {
        /// <summary>
        /// Speaks the screen and its state when the mod manager opens.
        ///
        /// Postfix on Open() rather than on the state change, because Open() is where the game
        /// decides what the screen CONTAINS (it runs Init() on first entry, Refresh() after), and
        /// because the state does not become MOD_MENU until a 0.5 s fade completes (:118-126) -
        /// announcing on the state change would speak half a second after the screen appeared.
        ///
        /// ⚠ Read the counts AFTER the base method, never before: on the very first open, Init()
        /// is what populates both lists, so a prefix would report an empty screen every time.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ModManagerUI), nameof(TG_ModManagerUI.Open))]
        public static void AfterOpen(TG_ModManagerUI __instance)
        {
            try
            {
                if (__instance == null) return;

                // Repair the screen's navigation BEFORE announcing it, so that by the time focus
                // recovery runs (~1 s later, after the open fade) the dead ends are already wired
                // and it cannot strand the player on one.
                RepairNavigation(__instance);

                int available = CountEntries(__instance, "allModListUI");
                int active = CountEntries(__instance, "currentActiveModListUI");

                // ⚠ KEEP THIS SHORT, AND SAY NOTHING THE FOCUS LINE WILL SAY.
                //
                // Every announcement in this mod passes interrupt:true (UalAnnouncer:54 does
                // Stop() then Output()), so FocusRecovery selecting the close button ~110 ms later
                // CUTS THIS OFF MID-SENTENCE. The first version was three sentences of key hints
                // and the player heard a fragment of it collide with "Close mod manager" - reported
                // as "it conflicts with what's spoken", and they were right.
                //
                // So this states only what the FOCUS LINE CANNOT: which screen this is, and how
                // many mods are in it. Control names and keys belong to the controls themselves.
                string spoken = available <= 0
                    ? "Mod manager, empty."
                    : "Mod manager, " + available + " mod" + (available == 1 ? "" : "s")
                      + ", " + active + " active.";

                Speak(spoken);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModMenu] open hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Counts the entries in one of the two list panels, via TG_ModContentListPanelUI.MaxUILength.
        ///
        /// Bound by STRING throughout: TG_ModContentListPanelUI does not exist in the demo assembly,
        /// and the mod builds against both. Same reason FocusRecovery's MOD_MENU scope is
        /// string-bound.
        /// </summary>
        private static int CountEntries(object manager, string fieldName)
        {
            object panel = AccessTools.Field(manager.GetType(), fieldName)?.GetValue(manager);
            if (panel == null) return 0;

            // ⚠ MaxUILength is a PROPERTY (TG_ModContentListPanelUI:53), not a field. AccessTools
            // .Field on a property returns null SILENTLY, which would make every list look empty and
            // send us down the "no mods installed" branch forever. See the field-vs-property trap
            // this codebase has already hit once.
            object len = AccessTools.Property(panel.GetType(), "MaxUILength")?.GetValue(panel, null);
            return len == null ? 0 : Convert.ToInt32(len);
        }

        /// <summary>
        /// Gives the close button a real caption.
        ///
        /// It announced as "Close Mod Button, unlabeled" - FocusNarrator correctly reporting a gap
        /// (the button carries no Text child at all, only an icon), but it is the FIRST thing heard
        /// on entering an empty mod menu, so the screen opened on a defect report.
        ///
        /// Registered as an override rather than by relabelling the object, so the game's own
        /// hierarchy is untouched and the mapping is visible in one place.
        /// </summary>
        internal const string CloseButtonName = "CloseModButton";
        internal const string CloseButtonLabel = "Close mod manager";

        /// <summary>
        /// Wires the two "all mods" buttons into the navigation graph, and takes the promo button
        /// OUT of it.
        ///
        /// ⚠ BOTH FIXES ARE TO SHIPPED SCENE DATA, not to code, which is why no amount of reading
        /// TG_ModManagerUI explains the symptom. Confirmed by an F10 dump taken inside MOD_MENU:
        ///
        ///   ModUIManager/ModManagerUICanvas/BlockPanel/CloseModButton              nav=Automatic
        ///   ModUIManager/.../BackgroundPanel/AddAllModButton     nav=Explicit up=- down=- left=- right=-
        ///   ModUIManager/.../BackgroundPanel/RemoveAllModButton  nav=Explicit up=- down=- left=- right=-
        ///   CanvasMainMenuUI/SafeArea/PromotionButton                              nav=Automatic
        ///
        /// 1. ADD-ALL / REMOVE-ALL ARE DEAD ENDS. Navigation.Mode.Explicit with all four targets
        ///    null means the EventSystem can never move off them - focus enters and cannot leave.
        ///    `SetNavigation` only ever wires the mod ROWS (TG_ModContentListPanelUI:236-250); these
        ///    two are never given targets at ANY list size, so this traps with 50 mods just as it
        ///    does with none. Reported live as "I can go down from there to remove all mods, but
        ///    that appears to trap the focus".
        ///
        ///    It also caused a RECOVERY LOOP that read as "focusing nothing": FocusRecovery selected
        ///    the dead-end button, the healthy-focus branch cleared its one-per-screen guard, the
        ///    selection was lost again, and the cycle repeated - seven "restored the control you
        ///    were on: AddAllModButton" lines in a row. Fixing the graph removes the cause; a guard
        ///    change would only have hidden it.
        ///
        /// 2. THE PROMO BUTTON IS NOT PART OF THIS SCREEN. It lives on CanvasMainMenuUI, the mod
        ///    menu's code never references it (grepped all four TG_Mod* classes: zero hits), and
        ///    TG_MainMenuManager owns its lifecycle and points it at a Steam store URL. It is
        ///    reachable only because it and CloseModButton are both nav=Automatic, which is
        ///    GEOMETRIC and canvas-blind - Unity picks the nearest selectable in the direction
        ///    pressed. Every other main-menu control is nav=None, which is exactly why nothing else
        ///    bleeds through and why this looked like it might belong to the screen.
        ///
        ///    ⚠ It is active at all only because opening the mod menu does NOT route through
        ///    DisableOtherMainMenu/SetActiveAnotherButton, so the main menu never hides its
        ///    satellites. Do not "fix" that by calling those - the main menu is mid-state and
        ///    hiding its buttons from under a screen it does not own is the mod fighting the game.
        ///
        /// Applied on Open (postfix) so it lands after Init/Refresh have built the lists, and
        /// re-applied rather than cached because the panels rebuild their graph whenever a mod is
        /// added or removed.
        /// </summary>
        private static void RepairNavigation(TG_ModManagerUI manager)
        {
            Selectable close = FindIn(manager, "closeButton");
            if (close == null) return;

            Selectable add = FindByName("AddAllModButton");
            Selectable remove = FindByName("RemoveAllModButton");

            // Give each dead end a way back to the close button, which is the one control on this
            // screen that is always present and always leads somewhere (its onClick is Close).
            // Deliberately NOT a full graph: with an empty list there is nothing else to point at,
            // and the game rebuilds the row graph itself whenever the list changes.
            LinkUp(add, close);
            LinkUp(remove, close);

            // Take the promo button out of reach WITHOUT deactivating it - the main menu owns that
            // object and will show/hide it on its own schedule. Mode.None leaves it clickable by
            // mouse and visible to sighted players; it simply stops being a navigation target.
            Selectable promo = FindByName("PromotionButton");
            if (promo != null && promo.navigation.mode != Navigation.Mode.None)
            {
                _promoPrevious = promo.navigation;
                _promoPatched = promo;
                Navigation off = promo.navigation;
                off.mode = Navigation.Mode.None;
                promo.navigation = off;
                MelonLogger.Msg("[ModMenu] promo button removed from navigation (belongs to the main menu).");
            }
        }

        /// <summary>
        /// The promo button's ORIGINAL navigation, restored when the mod menu closes.
        ///
        /// Without this the main menu would keep a promo button that no longer participates in
        /// navigation for the rest of the session - the mod having quietly broken a control on a
        /// screen it does not own. Anything this class disables, it must put back.
        /// </summary>
        private static Navigation _promoPrevious;
        private static Selectable _promoPatched;

        /// <summary>Restores the promo button's navigation when the screen closes.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ModManagerUI), nameof(TG_ModManagerUI.Close))]
        public static void AfterClose()
        {
            try
            {
                if (_promoPatched == null) return;
                _promoPatched.navigation = _promoPrevious;
                _promoPatched = null;
                MelonLogger.Msg("[ModMenu] promo button navigation restored.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModMenu] promo restore threw: " + e.Message);
            }
        }

        /// <summary>Points a dead-end button's up/down at a control that leads somewhere.</summary>
        private static void LinkUp(Selectable target, Selectable escape)
        {
            if (target == null || escape == null) return;

            Navigation nav = target.navigation;

            // Only repair genuine dead ends. If the game ever wires these itself - or a future build
            // does - leave its graph alone rather than overwriting a working one.
            if (nav.mode == Navigation.Mode.Explicit
                && nav.selectOnUp == null && nav.selectOnDown == null
                && nav.selectOnLeft == null && nav.selectOnRight == null)
            {
                nav.selectOnUp = escape;
                nav.selectOnDown = escape;
                target.navigation = nav;
                MelonLogger.Msg("[ModMenu] wired " + target.gameObject.name + " -> " + escape.gameObject.name);
            }
        }

        /// <summary>Reads a Selectable out of a private field on the manager.</summary>
        private static Selectable FindIn(object manager, string fieldName)
        {
            return AccessTools.Field(manager.GetType(), fieldName)?.GetValue(manager) as Selectable;
        }

        /// <summary>
        /// Finds an active Selectable by object name.
        ///
        /// By NAME because these two buttons are not exposed by any field or property on the
        /// manager or the panels - `invokeAllButton` is private on TG_ModContentListPanelUI and
        /// there is no accessor - and the names came from a live dump rather than from guessing.
        /// </summary>
        private static Selectable FindByName(string name)
        {
            Selectable[] all = UnityEngine.Object.FindObjectsOfType<Selectable>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].gameObject.activeInHierarchy && all[i].gameObject.name == name)
                    return all[i];
            }
            return null;
        }

        /// <summary>
        /// Reads a mod list entry by its DATA rather than by scanning its child Texts.
        ///
        /// ⚠ THIS IS WHY THE LIST WOULD HAVE READ THE WRONG FIELD. A TG_ModListItemUI carries THREE
        /// sibling Text components - modnameText, modDescrioptionText and modTagText
        /// (TG_ModListItemUI:8-14) - and FocusNarrator's generic path takes
        /// GetComponentInChildren&lt;Text&gt;(), which returns the FIRST in hierarchy order. That is
        /// whichever Unity happens to order first, not necessarily the name: the entry could have
        /// announced its description or its comma-joined tag list instead of what it is called.
        /// Nothing about the empty-list bug would have surfaced this - it only appears once real
        /// mods exist, which is exactly when it would have been hardest to attribute.
        ///
        /// TG_ModListItemUI exposes a public ModData property, and TG_DLCData derives from ModData,
        /// so reading Modname covers Workshop mods and DLC with one path and no guessing.
        /// </summary>
        internal static string ReadModEntryLabel(GameObject go)
        {
            if (go == null) return null;

            try
            {
                Type itemType = AccessTools.TypeByName("TG_ModListItemUI");
                if (itemType == null) return null;

                Component item = go.GetComponent(itemType) ?? go.GetComponentInParent(itemType);
                if (item == null) return null;

                object modData = AccessTools.Property(itemType, "ModData")?.GetValue(item, null);
                if (modData == null) return null;

                object name = AccessTools.Property(modData.GetType(), "Modname")?.GetValue(modData, null);
                string label = name?.ToString();
                if (string.IsNullOrEmpty(label)) return null;

                // The tag list is what distinguishes otherwise similarly-named mods, and it is the
                // one field a sighted player can see at a glance. Description is deliberately left
                // out: it is free text of arbitrary length and would bury the name on every move.
                object tags = AccessTools.Property(modData.GetType(), "ModsList")?.GetValue(modData, null);
                var list = tags as System.Collections.IList;
                if (list != null && list.Count > 0)
                {
                    string joined = "";
                    for (int i = 0; i < list.Count; i++)
                        joined += (i == 0 ? "" : ", ") + list[i];
                    if (!string.IsNullOrEmpty(joined)) label += ", " + joined;
                }

                return label;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModMenu] entry label threw: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Switches between the "all mods" and "active mods" tabs from the keyboard.
        ///
        /// The game binds this to LB/RB inside ControllerHandle, which is wrapped in
        /// `CurrentTypeControllerState == JOYSTICK` (:190) - so on a keyboard the second tab is
        /// unreachable and a player cannot see, let alone reorder, their active mods.
        ///
        /// Bracket keys because they read as "previous/next panel" and, unlike the obvious
        /// alternatives, are unbound: Q and E are already taken (the game binds Key.E/Key.Q onto
        /// RBButton/LBButton in JoystickPlayerActions:127-128), and Tab is the key that OPENS this
        /// screen. We call the game's own tab-switch sequence rather than reimplementing it.
        ///
        /// ⚠ Mirrors the game's own guard: it refuses to switch to the active list when that list is
        /// EMPTY (`currentActiveModListUI.MaxUILength > 0`, :204). Without that check the screen
        /// would move focus to a panel with nothing in it and strand the player.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ModManagerUI), "Update")]
        public static void AfterUpdate(TG_ModManagerUI __instance)
        {
            try
            {
                if (__instance == null) return;

                bool wantPrev = Input.GetKeyDown(KeyCode.LeftBracket);
                bool wantNext = Input.GetKeyDown(KeyCode.RightBracket);
                if (!wantPrev && !wantNext) return;

                // Only while the screen is actually interactive. Update() keeps running through the
                // open/close fades, when currentState is TWEENING and the lists are mid-rebuild.
                object mgr = AccessTools.TypeByName("TG_ControllerInputManager") == null
                    ? null
                    : TG_GenericSingelton<TG_ControllerInputManager>.Instance;
                if (mgr == null) return;
                object state = AccessTools.Field(mgr.GetType(), "currentState")?.GetValue(mgr);
                if (state == null || state.ToString() != "MOD_MENU") return;

                object allPanel = AccessTools.Field(__instance.GetType(), "allModListUI")?.GetValue(__instance);
                object activePanel = AccessTools.Field(__instance.GetType(), "currentActiveModListUI")?.GetValue(__instance);
                if (allPanel == null || activePanel == null) return;

                object target = wantNext ? activePanel : allPanel;

                // The game's own emptiness guard, mirrored - see the ⚠ above.
                if (wantNext && CountEntries(__instance, "currentActiveModListUI") <= 0)
                {
                    Speak("No active mods.");
                    return;
                }

                // Blur the panel we are leaving and clear the one we are entering, exactly as
                // ControllerHandle does, so the visuals match the focus for sighted players too.
                Invoke(wantNext ? allPanel : activePanel, "SetBlurImage", null);
                Invoke(target, "ClearBlurImage", null);

                AccessTools.Field(__instance.GetType(), "lastUITabActive")?.SetValue(__instance, target);
                Invoke(target, "SetFirstSelectedButton", null);

                Speak(wantNext ? "Active mods." : "All mods.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ModMenu] tab switch threw: " + e.Message);
            }
        }

        private static void Invoke(object target, string method, object[] args)
        {
            if (target == null) return;
            MethodInfo m = AccessTools.Method(target.GetType(), method);
            if (m == null)
            {
                MelonLogger.Warning("[ModMenu] method '" + method + "' not found.");
                return;
            }
            m.Invoke(target, args);
        }

        private static void Speak(string text)
        {
            MelonLogger.Msg("[ModMenu] " + text);
            ISpeechOutput speech = AccessMod.Speech;
            if (speech == null || !speech.IsAvailable) return;
            speech.SpeakAs(null, text, TextType.Menu, true);
        }
    }
}
