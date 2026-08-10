COFFEE TALK ACCESS
A screen reader mod for Coffee Talk
Version 0.8.1

This mod makes Coffee Talk speak through your screen reader. It reads menus,
the profile picker, the language picker, the name entry screen, the opening
cutscene, story dialogue, and the brewing screen, and it adds keyboard
navigation to menus that the game only lets you drive with a gamepad.

Works with NVDA, JAWS, SAPI, and braille displays, through UniversalSpeech.


WHAT IS NEW IN 0.8.1

  - Fixed the newspaper app sounding silent. It was reading out the date,
    the day number, the headline and how many paragraphs there were - and
    then the phone cut that sentence off after a word or two to say
    "Newspaper archive", which told you nothing. The reader now finishes.

  - The newspaper no longer reads as though a character were speaking it.

  - Note: early in the story the archive genuinely only has one day in it,
    and that day is the subscription teaser. It gets longer as you play.

WHAT IS NEW IN 0.8.0

This is the release where the full game actually works. Everything below was
tested by playing the retail game, not just by reading its code.

  - THE FULL GAME IS SUPPORTED AND PLAYED THROUGH. Version 0.6.0 was built
    against the demo and crashes on the full game's name entry screen, so if
    you have the full game you need this version.

  - Confirmed working by ear and in the logs: story dialogue, the brewing
    screen (ingredients, what each one adds, the glass, and the served
    drink), the profile picker, the language picker, name entry, the opening
    cutscene, pop-ups, the smartphone with its music and drink recipe apps,
    and the gallery and comics.

  - The smartphone works. The demo blocks it entirely, so every phone screen
    is new here. Music announces the track, artist and album; drink recipes
    announce each category with how many are still locked.

  - The profile picker is new, and it is the first screen the full game shows
    you. It was previously silent with dead arrow keys, which left no way
    past it without a gamepad.

  - Many smaller fixes found by playing: the picker no longer opens the wrong
    profile or jumps to a different card, switching between keyboard and
    gamepad is far quieter, and the phone no longer hands you a coffee
    ingredient instead of the phone.

  - Not yet exercised in play, so treat as unproven: the chat log, the
    save/load calendar, the social media app, and the ending epilogues.
    Reports on these are especially welcome.

WHAT YOU NEED

  - Coffee Talk, either the full game or the demo, installed through Steam.
  - MelonLoader v0.7.1, 32-bit (x86) version.
  - A screen reader running, or SAPI as a fallback.


IMPORTANT: MELONLOADER MUST BE THE 32-BIT VERSION

Coffee Talk is a 32-bit game. If you install the 64-bit MelonLoader, the mod
will not load at all. In the MelonLoader installer, set the architecture to
x86 before you install. After installing, you can confirm it in
MelonLoader\Latest.log, which will contain a line reading "Game Arch: x86".


HOW TO INSTALL

1. Install MelonLoader v0.7.1 (x86) into your Coffee Talk folder. That folder
   is normally one of:

   C:\Program Files (x86)\Steam\steamapps\common\Coffee Talk
   C:\Program Files (x86)\Steam\steamapps\common\Coffee Talk Demo

   Yours may be on another drive. In Steam you can find it with:
   right-click the game, then Manage, then Browse local files. It is the
   folder containing CoffeeTalk.exe.

2. Run the game once and then close it. This lets MelonLoader create its
   folders, including the Mods folder.

3. From this package, copy the two files inside the "Mods" folder into the
   game's "Mods" folder:

     CoffeeTalkAccess.dll
     UnityAccessibilityLib.dll

4. From this package, copy the two files inside the "GameRoot" folder into the
   game's main folder, the one that has CoffeeTalk.exe in it:

     UniversalSpeech.dll
     nvdaControllerClient.dll

   These two must sit next to CoffeeTalk.exe, NOT in the Mods folder. If you
   put them in Mods, speech will either be silent or quietly fall back to SAPI
   instead of using NVDA.

5. Start the game. You should hear "Coffee Talk Access loaded. Press F8 to
   test speech."


KEYS THE MOD ADDS

  Arrow keys or WASD   Move through menus. The game by itself only reads these
                       from a gamepad, so the mod supplies them.
  Enter                Activate the selected menu item.
  Escape               Back out, and answer "no" to a yes/no popup.
  Backquote  ( ` )     Repeat the last thing that was spoken. This is the key
                       to the left of the 1 key, above Tab.
  F8                   Speech test. Speaks a test line so you can check the
                       speech channel is alive.
  F9                   On the brewing screen, speak the current drink's stats.
  F10                  Dump the current interface state into the log. This is
                       for reporting problems, and it speaks nothing.


THE PROFILE PICKER (FULL GAME)

The full game opens on a row of three save profiles before the main menu.

  Left and right       Move between the three profiles.
  Enter                Open the highlighted profile. The card turns over to
                       show its options, and the mod announces them.
  Enter (again)        Load the opened profile.
  Escape               Close the opened profile and go back to the row.

Deleting a profile is the gamepad X button only; the game has no key for it.
Note that Enter means "open this card" on the row and "load" once a card is
open, because the card has two sides. The mod says which side you are on.


IF YOU USE A CONTROLLER

The game's own gamepad support works and the mod does not interfere with it,
but two settings matter:

  - If you use DS4Windows, set the output profile to X360, not PlayStation 4.
    The game's input library only recognizes two exact PlayStation controller
    names and has no fallback, while its Xbox profile matches almost anything.

  - If you have a DualSense, hide the raw controller in HidHide and whitelist
    DS4Windows. Otherwise Windows exposes the pad twice, the unmapped copy
    wins the game's "most recently used device" check, and the D-pad appears
    dead while the face buttons still work.


IF SOMETHING GOES WRONG

The log is the fastest way to tell what happened. It is at:

  MelonLoader\Latest.log

inside your game folder. It is a plain text file.

  No speech at all, and no "loaded" message
      MelonLoader probably is not running, or it is the 64-bit build. Check
      the log for "Game Arch: x86".

  The "loaded" message speaks, but nothing else does
      Check that UniversalSpeech.dll and nvdaControllerClient.dll are in the
      game's main folder next to CoffeeTalk.exe, and not in Mods.

  Speech works but sounds like SAPI instead of NVDA
      Same cause: nvdaControllerClient.dll is missing from the game's main
      folder, or is the 64-bit build.

  Menus do not respond to the arrow keys
      Look in the log for the line "All expected hooks are live". If a hook is
      named as missing, include that line in your report.

When reporting a problem, the most useful thing you can send is the log file,
plus which screen you were on and what you pressed.


KNOWN LIMITS

  - No one has finished a whole playthrough with this mod yet. The early game
    is well tested; the later it gets, the less it has been exercised.
  - These screens have never run in play and may have rough edges: the chat
    log, the save/load calendar, the social media app, and the ending
    epilogues. Reports on them are genuinely useful.
  - Switching TO a gamepad jumps the cursor to the first profile. That is the
    game moving its own cursor; it does the same without the mod.
  - Latte art is a drawing minigame. Only whether you served the drink is
    scored, not the picture, so the mod lets you serve without drawing. The
    drawing itself is still not accessible.
  - On the profile picker, deleting a profile needs a gamepad (X button). The
    game provides no keyboard key for it, and the mod does not invent one for
    an action that cannot be undone.
  - The chat log opens with the gamepad Y button only.
  - The Steam Workshop mod manager screen is announced but has not been tested.
  - Artwork in the gallery and comics is not described.


CREDITS

Mod by amock. Speech through UnityAccessibilityLib and UniversalSpeech.
Coffee Talk is by Toge Productions.


LICENSE

This mod is free software under the GNU General Public License, version 3 or
later. You may use, share and modify it, and the source is available at:

  https://github.com/ObjectInSpace/coffee-talk-access

It comes with NO WARRANTY. See the license for the full terms:

  https://www.gnu.org/licenses/gpl-3.0.txt

The license covers the mod itself. UniversalSpeech.dll and
nvdaControllerClient.dll are separate third-party components under their own
licenses, and Coffee Talk itself belongs to Toge Productions.
