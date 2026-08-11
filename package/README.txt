COFFEE TALK ACCESS
A screen reader mod for Coffee Talk
Version 0.9.0

This mod makes Coffee Talk speak through your screen reader. It reads menus,
the profile picker, the language picker, the name entry screen, the opening
cutscene, story dialogue, and the brewing screen, and it adds keyboard
navigation to menus that the game only lets you drive with a gamepad.

Works with NVDA, JAWS, SAPI, and braille displays, through UniversalSpeech.


WHAT IS NEW IN 0.9.0

  - The exit and mod manager buttons are reachable. The main menu has three
    buttons that sit outside the list the cursor walks - exit, mods, and a
    Steam promotion - so no amount of arrowing could ever land on them. The
    game offers them as gamepad shortcuts advertised by on-screen prompts,
    which is no help by ear. Escape now offers to quit from the main menu,
    and Tab opens the mod manager.

  - The mod manager is readable. It announces itself and how many mods you
    have, labels its close button, and adds the bracket keys for switching
    between the "all mods" and "active mods" lists - something the game
    offered only on a gamepad. See THE MOD MANAGER below.

  - Fixed the cursor getting stuck on the mod manager's "add all" and "remove
    all" buttons, which shipped with no way to move off them, and on a mod row
    when only one mod is installed - the game wires the rows into a ring that
    wraps around to itself with no way out.

  - Enter on a mod now actually turns it on or off, and says which. The game's
    own keyboard path only played the click sound without doing anything, so
    there was no way to tell whether a press had worked. A mouse click was
    unaffected, which is why this was never noticed.

WHAT IS NEW IN 0.8.3

  - The phone's apps now say what they are. They are named like real products
    - "Tomodachill", "Shuffld", "Brewpad", "The Evening Whisperss" - which
    tells you nothing by ear, so the mod now reads "Tomodachill, social
    media" and so on. See THE SMARTPHONE below for the full list.

  - Social media is confirmed working, both the friend list and a character's
    profile ("Gala. Birthday: 13 September. 2 more things to learn").

WHAT IS NEW IN 0.8.2

  - Fixed confirmation dialogs not taking focus. Loading a save asks "do you
    want to restart the day?", but the arrow keys still moved the calendar
    behind the dialog instead of choosing Yes or No. The dialog now takes
    focus as soon as it appears. Enter still means yes and Escape still
    means no, as before.

  - The save/load calendar is confirmed working: it reads each day, whether a
    save exists, and the last-played summary.

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

  - Not yet exercised in play, so treat as unproven: the chat log and the
    ending epilogues. Reports on these are especially welcome.


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
  Escape               Back out, and answer "no" to a yes/no popup. On the main
                       menu there is nothing to back out to, so Escape offers to
                       quit the game. It asks first, and the answer starts on
                       "no", so a mistaken press costs you one keypress.
  Tab                  On the main menu, open the mod manager. This matches Tab
                       opening the phone during the game. In the cafe Tab is
                       still the phone.
  Backquote  ( ` )     Repeat the last thing that was spoken. This is the key
                       to the left of the 1 key, above Tab.
  Tilde  ( ~ )         The SAME key with shift held. Turns the reading of the
                       in-game conversation on and off, and says which it just
                       switched to. Use it if you already know the story, or
                       want to listen to the game's own audio undisturbed.
                       Everything else keeps speaking either way: menus, brewing,
                       the chat log, and the opening cutscene. The cutscene is
                       left alone on purpose, because its lines disappear on a
                       timer and there is no way to go back for them. The repeat
                       key above also still works while reading is off.
  F8                 Speech test. Speaks a test line so you can check the
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


THE MOD MANAGER (FULL GAME)

Press Tab on the main menu. This is the Steam Workshop mod manager, and it is
separate from mods like this one that you install by hand -- only Workshop
subscriptions appear here.

  Up and down          Move through the mod list.
  Enter                Turn a mod on or off.
  [ and ]              Switch between the "all mods" and "active mods" lists.
                       The game only offers this on a gamepad's shoulder
                       buttons, so the mod supplies the bracket keys.
  Tab                  Close the mod manager, the same key that opened it.

You turn a mod ON from the "all mods" list and OFF from the "active mods"
list, so pressing Enter twice on the same row will not undo it -- press ] to
reach the active list, then Enter on the mod there.

If you have no Workshop mods subscribed, the screen says "Mod manager, empty"
and holds only the close and "add all" buttons. The "add all" and "remove all"
buttons make a click sound and do nothing when the list is empty; that is the
game's own behaviour and not a fault in the mod.

Two notes about this screen, both worked around rather than fixed at source,
because the problems are in the game's data rather than its code. The "add all"
and "remove all" buttons ship with no way to move off them, which trapped the
cursor; the mod points them back at the close button. And a Steam promotion
button belonging to the main menu underneath is close enough on screen that the
cursor could wander onto it; the mod takes it out of the running while the mod
manager is open, and puts it back afterwards.


THE SMARTPHONE (FULL GAME)

The phone's four apps are named like real products, so the mod now says what
each one does as well:

  Tomodachill, social media    Your friend list. Up and down for characters,
                               Enter for a character's profile and trivia.
  Shuffld, music               The music player. Enter plays a track.
  Brewpad, drink recipes       Recipes by category, with how many are locked.
  The Evening Whisperss,       The newspaper archive. Down arrow reads the
    newspaper                  article, left and right change days.

Escape backs out of an app, and again to close the phone.


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
    log and the ending epilogues. Reports on them are genuinely useful.

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
