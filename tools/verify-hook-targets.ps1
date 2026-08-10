# Resolves every string-named reflection target the mod uses against a shipped
# Assembly-CSharp.dll. A miss here is a hook that returns null SILENTLY at runtime --
# the failure mode that looks identical to a working build.
#
# The nameof(...) hook targets are already proven by the project compiling. This covers
# the ~120 lookups bound by STRING, which the compiler cannot check.
#
# USAGE
#   .\tools\verify-hook-targets.ps1                      # against the demo (default)
#   .\tools\verify-hook-targets.ps1 -GameDir "<path>"    # against RETAIL -- the Phase 6 task
#
# Expected against the demo as of 2026-08-10: 46/46 types, 89/89 members, 0 missing.
# Anything MISSING on retail is a screen that will be mute with a clean log.

param(
    [string]$GameDir = "D:\SteamLibrary\steamapps\Common\Coffee Talk Demo"
)

$ErrorActionPreference = 'Stop'
$managed = Join-Path $GameDir 'CoffeeTalk_Data\Managed'
if (-not (Test-Path (Join-Path $managed 'Assembly-CSharp.dll'))) {
    throw "No Assembly-CSharp.dll under $managed - check -GameDir."
}
Write-Output "Auditing: $managed"

Add-Type -AssemblyName System.Runtime
$resolver = {
    param($s, $e)
    $simple = $e.Name.Split(',')[0]
    $p = Join-Path $managed "$simple.dll"
    if (Test-Path $p) { return [System.Reflection.Assembly]::LoadFrom($p) }
    return $null
}
[System.AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve($resolver)
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)

$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $managed 'Assembly-CSharp.dll'))
Write-Output ("Loaded: " + $asm.GetName().Name + " runtime " + $asm.ImageRuntimeVersion)

$B = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static,FlattenHierarchy'

function Resolve-GameType { param([string]$n)
    $t = $asm.GetType($n)
    if ($null -eq $t) { $t = $asm.GetTypes() | Where-Object { $_.Name -eq $n } | Select-Object -First 1 }
    return $t
}

# ---- 1. Types looked up by string (AccessTools.TypeByName) ----
$typeNames = @(
 'TG_ControllerInputManager','TG_GenericSingelton`1','TG_Static','TG_FriendListPrefabUI',
 'TG_AchievementIconUI','TG_CalendarLoadUI','TG_DrinkItemUI','TG_PlaylistSongUI','TG_InitScreen',
 'TG_GameManager','TG_MusicApp','TG_MusicAppDemo','TG_MusicAppGeneral','TG_SmartPhoneApps',
 'TG_NewspaperApp','TG_SocialMediaDetailProfileUI','TG_DrinkRecipesApp','TG_ComicDisplayUI',
 'TG_SmartPhoneManager','TG_ChatLogManager','TG_NewspaperManager','TG_EndingCutsceneItem',
 'TG_AchievementMenuManager','TG_CalendarUIManager','TG_SaveMenuManager','TG_GalleryManager',
 'TG_ComicMenuManager','TG_MainMenuManager','TG_MainMenuButton','TG_NameKeys','TG_PopUpUI',
 'TG_PopUpLoadUI','TG_UIMenuContent','TG_DrinkManager','LatteArtManager','TG_CutsceneManager',
 'TG_OpeningCutSceneManager','TG_PressAnyBlinkTextUI','TG_PressAnyToSkipBlinkTextUI',
 'TG_KeyboardHotkeyManager','TG_PopUpManager','TG_ToolTipManager','TG_EndlessModeDialogManager',
 'TG_SocialMediaApp'
)
Write-Output "`n===== TYPES ====="
$missingTypes = @()
foreach ($n in $typeNames) {
    $t = Resolve-GameType $n
    if ($t) { Write-Output ("  OK      " + $n) }
    else { Write-Output ("  MISSING " + $n); $missingTypes += $n }
}

# ---- 2. Members looked up by string: "Type|member|kind" ----
$members = @(
 'TG_ControllerInputManager|currentState|F','TG_ControllerInputManager|CheckRemovedState|M',
 'TG_ControllerInputManager|OptionPlusMinusButtonHandler|M','TG_ControllerInputManager|ControllerUpdateFunction|M',
 'TG_ControllerInputManager|HandlerControllerPress|M','TG_ControllerInputManager|AButtonPressed|M',
 'TG_ControllerInputManager|playerActions|F','TG_ControllerInputManager|currentTypeController|F',
 'TG_ControllerInputManager|keyboardHotkeyManager|F',
 'TG_InitScreen|languageSettingGO|F','TG_Static|languageList|F','TG_Static|userSettingsData|F',
 'TG_Static|allCharacterDataList|F','TG_Static|profileData|F','TG_Static|localizer|F',
 'TG_Static|CheckContainsUnlockedCharacterDataIdx|M',
 'TG_DrinkManager|brewList|F','TG_DrinkManager|drinkName|F','TG_DrinkManager|drinkNameText|F',
 'TG_DrinkManager|ingredientsLocList|F','TG_DrinkManager|glass_value|F','TG_DrinkManager|statsBarManager|F',
 'TG_DrinkManager|GetDrinkNameAndColor|M','TG_DrinkManager|AddIngredient|M','TG_DrinkManager|ServeGlassDrink|M',
 'TG_DrinkManager|ResetIngredients|M','TG_DrinkManager|SetPreviewStatsDrinkUI|M','TG_DrinkManager|BrewInformationClick|M',
 'TG_MainMenuManager|buttonList|F','TG_MainMenuManager|cursorIdx|F','TG_MainMenuManager|optionButtonPanel|F',
 'TG_MainMenuManager|cursorMenuIdx|F','TG_MainMenuManager|MouseOverManager|M',
 'TG_GameManager|optionsUIManager|F','TG_GameManager|chatLogManager|F',
 'TG_ChatLogManager|GetStoryText|M','TG_ChatLogManager|GetCharacterNameLocalization|M',
 'TG_ChatLogManager|StartOpenChatLog|M','TG_ChatLogManager|CloseChatLog|M',
 'TG_AchievementMenuManager|achievementProgressNumberText|F','TG_AchievementMenuManager|achievementIconUIs|F',
 'TG_AchievementMenuManager|SetSelectedData|M','TG_AchievementMenuManager|Init|M',
 'TG_AchievementMenuManager|BackToExtrasMenu|M','TG_AchievementIconUI|unclocked|F',
 'TG_CalendarUIManager|selectedDataUI|F','TG_CalendarUIManager|lastPlayedDataUI|F',
 'TG_CalendarUIManager|SetSelectedData|M','TG_CalendarUIManager|SetLastPlayedData|M',
 'TG_SaveMenuManager|BackToMainMenu|M','TG_SaveMenuManager|BackToPauseInGameMenu|M',
 'TG_ComicDisplayUI|scriptableComicData|F',
 'TG_MusicApp|PlaylistSongButtonClick|M','TG_MusicAppDemo|PlaylistSongButtonClick|M',
 'TG_SmartPhoneApps|Open|M','TG_SmartPhoneApps|Close|M',
 'TG_SmartPhoneManager|OpenSmartPhone|M','TG_SmartPhoneManager|canOpenSmartPhone|F',
 'TG_NewspaperApp|SetNewsOnApp|M','TG_NewspaperApp|Open|M',
 'TG_NewspaperManager|GenerateNewspaper|M','TG_DrinkRecipesApp|DisplayDrinks|M',
 'TG_DrinkItemUI|OnButtonClick|M','TG_DrinkItemUI|drinkName|F','TG_DrinkItemUI|drinkIngredients|F',
 'TG_DrinkItemUI|drinkDescriptionObject|F',
 'TG_GalleryManager|SetLargeImage|M','TG_GalleryManager|SetBiggestImage|M',
 'TG_ComicMenuManager|SetLargeImage|M','TG_ComicMenuManager|SetBiggestImage|M',
 'TG_NameKeys|Initialize|M','TG_NameKeys|OnValueChanged|M','TG_NameKeys|ConfirmName|M','TG_NameKeys|BackToMainMenu|M',
 'TG_PopUpUI|SetPopUpTitleText|M','TG_PopUpUI|SetTextTerm|M','TG_PopUpLoadUI|SetInfoText|M',
 'TG_UIMenuContent|EventHoverMouse|M','TG_MainMenuButton|MouseHover|M',
 'TG_EndingCutsceneItem|GetDialogueEndingCutscene|M',
 'TG_OpeningCutSceneManager|SetDialogueText|M','TG_OpeningCutSceneManager|GetCreditOpeningText|M',
 'TG_CutsceneManager|SetDialogueText|M','TG_CutsceneManager|currentWholeText|F',
 'TG_PressAnyBlinkTextUI|SetTextLocalization|M','TG_PressAnyToSkipBlinkTextUI|SetTextLocalization|M',
 'TG_PressAnyBlinkTextUI|blinkTextArea|F',
 'LatteArtManager|ActivateLatteArt|M','LatteArtManager|CloseLatteArt|M'
)
Write-Output "`n===== MEMBERS ====="
$missingMembers = @()
foreach ($spec in $members) {
    $p = $spec.Split('|'); $tn = $p[0]; $mn = $p[1]; $kind = $p[2]
    $t = Resolve-GameType $tn
    if (-not $t) { Write-Output ("  NOTYPE  " + $spec); $missingMembers += $spec; continue }
    $found = $false; $extra = ''
    if ($kind -eq 'M') {
        $ms = $t.GetMethods($B) | Where-Object { $_.Name -eq $mn }
        if ($ms) { $found = $true; $d = ($ms | Select-Object -First 1).DeclaringType.Name
                   if ($d -ne $t.Name) { $extra = " (inherited from $d)" } }
    } else {
        $f = $t.GetField($mn, $B)
        if ($f) { $found = $true }
        else {
            $pr = $t.GetProperty($mn, $B)
            if ($pr) { $found = $true; $extra = ' *** IS A PROPERTY, NOT A FIELD -> AccessTools.Field returns NULL ***' }
        }
    }
    if ($found) { Write-Output ("  OK      " + $spec + $extra) }
    else { Write-Output ("  MISSING " + $spec); $missingMembers += $spec }
}

# ---- 3. Base-class hooks: does an EMPTY VIRTUAL or an override without base() silently
#         neuter them? This is the failure mode that attaches, reports GREEN and never fires.
Write-Output "`n===== BASE-CLASS HOOK SAFETY ====="
$D = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,DeclaredOnly'
foreach ($pair in @('TG_SmartPhoneApps|Open','TG_SmartPhoneApps|Close','TG_CutsceneManager|SetDialogueText','TG_UIMenuContent|EventHoverMouse')) {
    $p = $pair.Split('|'); $bt = Resolve-GameType $p[0]; $mn = $p[1]
    if (-not $bt) { Write-Output ("  NOTYPE  " + $pair); continue }
    $bm = $bt.GetMethod($mn, $D)
    if (-not $bm) { Write-Output ("  base does not declare " + $pair); continue }
    $il = $bm.GetMethodBody().GetILAsByteArray().Length
    $note = if ($il -le 1) { '  *** EMPTY VIRTUAL - a postfix here NEVER fires; hook the CONCRETE classes ***' } else { '' }
    Write-Output ("  {0}.{1}  virtual={2} IL={3}{4}" -f $bt.Name, $mn, $bm.IsVirtual, $il, $note)
    $subs = $asm.GetTypes() | Where-Object { $_ -ne $bt -and $bt.IsAssignableFrom($_) }
    foreach ($s in ($subs | Sort-Object Name)) {
        $om = $s.GetMethod($mn, $D)
        if ($om) { Write-Output ("      {0,-26} OVERRIDES (IL {1}) -> base hook fires ONLY if it calls base.{2}()" -f $s.Name, $om.GetMethodBody().GetILAsByteArray().Length, $mn) }
        else     { Write-Output ("      {0,-26} inherits -> base hook covers it" -f $s.Name) }
    }
}

Write-Output "`n===== SUMMARY ====="
Write-Output ("Types checked:   " + $typeNames.Count + "  missing: " + $missingTypes.Count)
Write-Output ("Members checked: " + $members.Count + "  missing: " + $missingMembers.Count)
if ($missingTypes)   { Write-Output "MISSING TYPES:";   $missingTypes   | ForEach-Object { "   $_" } }
if ($missingMembers) { Write-Output "MISSING MEMBERS:"; $missingMembers | ForEach-Object { "   $_" } }
