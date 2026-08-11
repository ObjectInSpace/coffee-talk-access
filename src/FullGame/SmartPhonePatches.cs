using System;
using System.Text;
using CoffeeTalkAccess.Dialogue;
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
    /// Announces the in-game smartphone and its four apps.
    ///
    /// ⚠ THE PHONE OPENS ON THE DEMO BUT IS BLOCKED - measured 2026-08-10, superseding an earlier
    /// note in this file that read "the phone IS reachable on the demo, Tab opens it". That note
    /// was written from a live report of hearing phone words and was never checked against the
    /// log; it is wrong, and it misled a later session within a minute of being read.
    ///
    /// What the logs actually show (26-8-10_2-4-50, 26-8-10_2-21-28): OpenSmartPhone fires five
    /// times, and NOT ONCE does an app open - no TG_SmartPhoneApps.Open hook has ever run. The
    /// cause is in the method itself: canOpenSmartPhone is false, so it takes the ELSE branch,
    /// raises screenBlockPanel, and calls CloseUnaccesablePhone() to slide the phone back off
    /// screen ~0.3s later. The demo denies the phone by design, in-fiction (that branch also runs
    /// a Fungus block, presumably the "not now" line).
    ///
    /// CONSEQUENCE FOR THE CONTENT PANES BELOW: they cannot be developed or tested on the demo,
    /// because the four app panels never activate. That is a full-game task and needs the retail
    /// binary. Do not write cursors against them here - they would attach cleanly, log nothing,
    /// and do nothing, which is this project's most expensive failure mode.
    ///
    /// The lesson worth keeping: a hook firing is not the same as a screen working. Judge the
    /// OUTCOME, not the entry.
    ///
    /// PRIOR ART CALLED THE SMARTPHONE "KNOWN-HARD". Reading it, the difficulty is narrower than
    /// that suggests, and it is worth writing down precisely so the next session does not
    /// re-litigate it:
    ///  - The HOME screen is ordinary Buttons with Select() calls (TG_SmartPhoneManager:250, :361).
    ///    ⚠ CORRECTED 2026-08-10 after a live report: those Select() calls are gated on JOYSTICK
    ///    mode, so on a keyboard the phone opens with NOTHING selected and is entirely unnavigable.
    ///    FocusNarrator can only narrate a focus that exists; PhoneFocusWatcher now supplies it.
    ///  - The FRIEND LIST is also EventSystem-driven: TG_SocialMediaApp.ButtonInput walks
    ///    FriendListButton.navigation.selectOnUp/selectOnDown and calls SetSelectedGameObject. So
    ///    focus IS observable; the only gap is that a friend entry's text lives on a
    ///    TG_FriendListPrefabUI (ProfileNameText / ProfileInfoText), not on the Button, so the
    ///    generic label scan can miss it. That is a labelling fix, handled below.
    ///  - The genuinely hard part is the CONTENT PANE: it is scrolled by a Scrollbar
    ///    (ScrollDetailProfile / ScrollNews), exactly like the chat log - analog position, no
    ///    per-item focus. Reading that well needs an entry cursor over the underlying data, the
    ///    same shape as ChatLogPatches, and it is deliberately NOT attempted here: the data source
    ///    differs per app, and building four cursors blind against a build we cannot run would be
    ///    guesswork stacked on guesswork. It is listed in PLAN.md as the follow-up.
    ///
    /// So this file does the part that is knowable offline - announce which app opened, and label
    /// friend entries - and honestly leaves the scrolling panes for a session that can test them.
    /// </summary>
    [HarmonyPatch]
    public static class SmartPhonePatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>
        /// Announces the phone opening. The player needs to know the phone took over input, since
        /// the underlying screen stops responding.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_SmartPhoneManager), nameof(TG_SmartPhoneManager.OpenSmartPhone))]
        public static void AfterOpenSmartPhone(TG_SmartPhoneManager __instance)
        {
            try
            {
                // OpenSmartPhone returns early in several states (TWEENING, VIEWING_GLASS, when the
                // canvas is not interactable, or while already tweening). Announcing regardless
                // would describe a phone that never opened.
                if (__instance == null) return;

                // ⚠ canOpenSmartPhone false does NOT mean "no phone happened" - it means the game
                // took OpenSmartPhone's ELSE branch: the phone still animates in and the state
                // still becomes SMART_PHONE/PHONE_HOME, but screenBlockPanel goes up and
                // CloseUnaccesablePhone() pulls it back off screen ~0.3s later.
                //
                // This previously returned early on that branch and then announced the app list
                // anyway from the shared path below, so a blocked phone said "Social media, music,
                // drink recipes, newspaper" - four things the player could not reach. Naming
                // absent options is worse than silence: it sends a blind player hunting for a
                // control that does not exist. Say what actually happened instead.
                if (!__instance.canOpenSmartPhone)
                {
                    Announce("Smartphone unavailable right now.");
                    return;
                }

                // ⚠ DO NOT ANNOUNCE HERE - THE PHONE IS NOT OPEN YET.
                //
                // OpenSmartPhone only STARTS a 0.6 s tween (smartPhonePanel.DOAnchorPosY(10f, 0.6f))
                // and returns. Everything that makes the phone real happens in that tween's
                // OnComplete: ChangeGameState(GameState.SMART_PHONE), the entry Select(), and
                // SetSwitchCursorSmartphone(). So a POSTFIX here runs while the state is still
                // TWEENING and the café underneath still owns focus.
                //
                // Live proof, log 26-8-10_18-17-43: "[Phone] Smartphone..." at 18:21:11.696 and
                // "[Focus] Coffee" - a BREWING ingredient - 82 ms later. The player heard the phone
                // announced and then found themselves on the brew pad. Reported as "phone navigation
                // doesn't seem to be working" AND "the brewpad didn't get initial focus": one cause,
                // two symptoms, because the announcement described a screen that did not exist yet.
                //
                // Deferred to a watcher polled from OnUpdate, which speaks when the game's own state
                // actually reports a PHONE_* screen. Same shape and same reason as
                // AchievementPatches.EntryWatcher and CalendarPatches' entry line: the screen becomes
                // live inside a DOTween callback with no method to postfix.
                PhoneEntryWatcher.Arm();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Phone] open hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Waits for the phone to be genuinely open before announcing it.
        ///
        /// ARMED by the OpenSmartPhone postfix, which fires ~0.6 s too early (see there). Polled
        /// from OnUpdate; speaks on the first frame the game's own state machine reports a PHONE_*
        /// screen, which is set inside the tween's OnComplete alongside the entry Select().
        ///
        /// Bounded rather than open-ended: an armed watcher that never saw its screen would
        /// eventually announce the phone over some unrelated later screen. Same shape as
        /// NameScreenWatcher and AchievementPatches.EntryWatcher.
        /// </summary>
        internal static class PhoneEntryWatcher
        {
            private static bool _armed;
            private static float _expiry;

            internal static void Arm()
            {
                _armed = true;
                // The tween is 0.6 s; allow generous margin for a slow frame without arming forever.
                _expiry = Time.realtimeSinceStartup + 5f;
            }

            internal static void Reset()
            {
                _armed = false;
            }

            /// <summary>
            /// Selects the app button the game selects for a gamepad, so the home screen has a
            /// cursor to arrow from on a keyboard.
            ///
            /// Reads socialMediaAppButton off the live manager rather than picking a control
            /// ourselves: it is the game's own first-selected button, so the cursor lands exactly
            /// where a pad player's would and the app grid's own navigation takes over from there.
            /// </summary>
            private static void SupplyHomeScreenSelection()
            {
                try
                {
                    if (EventSystem.current == null)
                    {
                        MelonLogger.Msg("[Phone] home-screen selection skipped: no EventSystem.");
                        return;
                    }

                    TG_SmartPhoneManager mgr = UnityEngine.Object.FindObjectOfType<TG_SmartPhoneManager>();
                    if (mgr == null)
                    {
                        MelonLogger.Msg("[Phone] home-screen selection skipped: no phone manager.");
                        return;
                    }

                    Button entry = mgr.socialMediaAppButton;
                    if (entry == null || !entry.gameObject.activeInHierarchy || !entry.interactable)
                    {
                        MelonLogger.Msg("[Phone] home-screen selection skipped: entry button not usable.");
                        return;
                    }

                    // ⚠ DO NOT SKIP ON "SOMETHING IS ALREADY SELECTED". That check was here and it
                    // made this whole hook a no-op: opening the phone from the brew pad leaves a
                    // LIVE ingredient button selected (log 26-8-11_16-45-15 - "Coffee" 84 ms before
                    // the phone announced), so `sel != null && sel.activeInHierarchy` was true every
                    // time and we returned without doing anything.
                    //
                    // The selection being alive does not mean it is RELEVANT - it belongs to the
                    // café behind the phone. Same stale-selection trap that broke the brewing entry
                    // seeding earlier today; the fix is the same, ask whether the selection is on
                    // THIS screen. Here that is one comparison, because the phone has exactly one
                    // entry control and we already have it.
                    GameObject sel = EventSystem.current.currentSelectedGameObject;
                    if (ReferenceEquals(sel, entry.gameObject))
                    {
                        MelonLogger.Msg("[Phone] home-screen selection already correct.");
                        return;
                    }

                    entry.Select();
                    entry.OnSelect(null);
                    MelonLogger.Msg("[Phone] supplied the keyboard's missing home-screen selection.");
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[Phone] home-screen selection threw: " + e.Message);
                }
            }

            internal static void Update()
            {
                try
                {
                    if (!_armed) return;

                    if (Time.realtimeSinceStartup > _expiry)
                    {
                        // The phone never came up. Say nothing: describing a screen that did not
                        // appear is worse than silence, and the log records the miss.
                        _armed = false;
                        MelonLogger.Msg("[Phone] open watcher expired without reaching a phone screen.");
                        return;
                    }

                    string state = AccessMod.ReadControllerState();
                    if (state == null || !state.StartsWith("PHONE")) return;

                    _armed = false;

                    // ⚠ SUPPLY THE GAME'S OWN ENTRY SELECTION - AND ONLY ON THE HOME SCREEN.
                    //
                    // TG_SmartPhoneManager.HomeScreenButton (:349) ends with
                    //     if (setCursor && CurrentTypeControllerState == JOYSTICK)
                    //     { socialMediaAppButton.Select(); socialMediaAppButton.OnSelect(null); }
                    // with no else - the same missing-else gate as SelectCocoa and the serve
                    // options. On a keyboard the phone opens with NOTHING selected, so the arrow
                    // keys have no cursor to move and focus stays on the café behind it. Measured,
                    // log 26-8-11_16-40-2: the phone announced at 16:41:53.397 and the next focus
                    // line is "Green tea" - an ingredient.
                    //
                    // This is a MISSING TRIGGER, not a missing cursor, and the distinction is the
                    // whole point: PHONE_HOME is one of the four states the game genuinely
                    // NAVIGATES (TG_ControllerInputManager:413 routes it to HandleResumeToSmartPhone),
                    // so a selection here is what the game itself would have made. We select the
                    // game's OWN choice - socialMediaAppButton - not a control we picked.
                    //
                    // ⚠ HOME SCREEN ONLY. The app panes (newspaper, and the other Scrollbar
                    // readers) are driven by their own UpdateFunction reading Up/Down directly, and
                    // planting a cursor there is what sent the player wandering out of the phone
                    // onto the brew pad. Do not widen this to PHONE_*. See FocusRecovery's
                    // "THE PHONE GETS NO CURSOR FROM US" note and NeedsSelection's whitelist.
                    // Logged unconditionally: when this hook did nothing, the ONLY question worth
                    // answering from the log is which state it actually saw, and a silent branch
                    // cannot answer it. This has already cost one live run.
                    MelonLogger.Msg("[Phone] entry watcher fired on state=" + state);

                    if (state == "PHONE_HOME") SupplyHomeScreenSelection();

                    Announce("Smartphone. Social media, music, drink recipes, newspaper.");
                }
                catch (Exception e)
                {
                    _armed = false;
                    MelonLogger.Warning("[Phone] entry watcher threw: " + e.Message);
                }
            }
        }

        /// <summary>
        /// Announces which app was opened.
        ///
        /// Patching the BASE class TG_SmartPhoneApps.Open covers all four apps at once - social
        /// media, music, drink recipes and newspaper all override it and call through. This is the
        /// same base-class trick OptionPatches uses on TG_UIMenuContent. (Contrast the cutscene
        /// case, where the base body was EMPTY and the override had to be patched instead: which
        /// one is right depends on where the real body lives, so it is checked each time rather
        /// than assumed.)
        ///
        /// The app is named from its TYPE rather than from a caption, because the caption is an
        /// icon label that may not exist as text.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_SmartPhoneApps), nameof(TG_SmartPhoneApps.Open))]
        public static void AfterAppOpen(TG_SmartPhoneApps __instance)
        {
            try
            {
                if (__instance == null) return;

                // ⚠ THE NEWSPAPER ANNOUNCES ITSELF - SAYING "Newspaper archive." HERE TALKS OVER IT.
                //
                // TG_NewspaperApp.Open calls SetNewsOnApp() on its way through, and
                // NewspaperAppPatches postfixes that to speak the real header: the date, which day
                // of how many, the headline, the paragraph count and the keys. This hook then fired
                // immediately afterwards with interrupt:true and cut that sentence off, replacing
                // it with two words that carry none of the same information.
                //
                // Live proof, log 26-8-10_18-17-43 at 18:20:52 - the full header is in the log, and
                // 40 ms later "[Phone] Newspaper archive." interrupts it. The player reported
                // "I tried the newspaper but didn't hear anything", which is what a sentence
                // truncated after a word or two sounds like.
                //
                // The other three apps have no such reader (music and recipes announce per-ROW, on
                // focus, which is a different moment) so they still want this line.
                if (__instance is TG_NewspaperApp) return;

                string app = DescribeApp(__instance.GetType().Name);
                if (string.IsNullOrEmpty(app)) return;

                Announce(app);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Phone] app hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Maps an app class name to something worth hearing, including how it is read where that
        /// is not obvious.
        ///
        /// An unrecognized app still gets announced (prettified from its type name) rather than
        /// silently ignored - a new app in the retail build should be audible as "something opened
        /// that the mod does not know about", not as nothing at all.
        /// </summary>
        private static string DescribeApp(string typeName)
        {
            switch (typeName)
            {
                case "TG_SocialMediaApp":
                    return "Social media. Up and down to move through the friend list.";
                case "TG_MusicApp":
                case "TG_MusicAppGeneral":
                    return "Music player.";
                case "TG_DrinkRecipesApp":
                    return "Drink recipes.";
                case "TG_NewspaperApp":
                    return "Newspaper archive.";
                default:
                    if (string.IsNullOrEmpty(typeName)) return null;
                    // Strip the TG_ prefix and the App suffix, then space the camel case.
                    string name = typeName.StartsWith("TG_") ? typeName.Substring(3) : typeName;
                    return Prettify(name) + ", unlabeled app.";
            }
        }

        /// <summary>
        /// Reads a friend-list entry's name and info.
        ///
        /// Exposed for FocusNarrator: the entry's text is on the TG_FriendListPrefabUI component
        /// (ProfileNameText / ProfileInfoText are properties, NOT fields - the decompile renders
        /// several of this game's properties as fields, which silently returns null through
        /// AccessTools.Field), while the focused object is the Button. Without this the friend list
        /// would announce as "unlabeled".
        /// </summary>
        internal static string DescribeFriendEntry(MonoBehaviour prefabUi)
        {
            try
            {
                if (prefabUi == null) return string.Empty;

                Type t = prefabUi.GetType();
                Text nameText = Read<Text>(prefabUi, t, "ProfileNameText", "profileNameText");
                Text infoText = Read<Text>(prefabUi, t, "ProfileInfoText", "profileInfoText");

                StringBuilder sb = new StringBuilder();
                if (nameText != null && !string.IsNullOrEmpty(nameText.text))
                    sb.Append(FungusText.ExtractWords(nameText.text));

                if (infoText != null && !string.IsNullOrEmpty(infoText.text))
                {
                    string info = FungusText.ExtractWords(infoText.text);
                    if (info.Length > 0)
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(info);
                    }
                }

                return sb.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Reads a member that may be a property OR a field, in that order.
        ///
        /// This ordering is deliberate and was paid for: TG_ProfileData.UnlockedCharacterDataList
        /// and TG_CharacterUnlockedData.IntroducedSelf both appear as fields in the decompiled
        /// source and are properties in the real assembly, and AccessTools.Field on a property
        /// returns NULL SILENTLY - a mod that runs, logs cleanly, and says the wrong thing.
        /// </summary>
        private static T Read<T>(object instance, Type type, string propertyName, string fieldName)
            where T : class
        {
            object value = AccessTools.Property(type, propertyName)?.GetValue(instance, null)
                           ?? AccessTools.Field(type, fieldName)?.GetValue(instance);
            return value as T;
        }

        /// <summary>Spaces a camel-case type name for speech.</summary>
        private static string Prettify(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            StringBuilder sb = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private static void Announce(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[Phone] " + line);
            speech.SpeakAs(null, line, TextType.Menu, true);
        }

    }
}
