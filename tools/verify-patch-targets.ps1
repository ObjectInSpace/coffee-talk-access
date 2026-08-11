# Resolves every [HarmonyPatch] target declared in the BUILT mod against a shipped game assembly.
#
# WHY THIS EXISTS, alongside verify-hook-targets.ps1. That script checks the targets a HUMAN listed;
# this one reads the attributes out of the COMPILED DLL, so it cannot drift from the code and it
# covers hooks nobody remembered to list. Together they answer the two halves of "will this build
# be silent": are the names right, and do the patches actually bind.
#
# This is what PatchAll does at boot. A target that does not resolve leaves the screen mute with a
# clean log -- this project's most expensive failure mode -- so doing it offline turns what used to
# cost a live run into a script.
#
# USAGE
#   .\tools\verify-patch-targets.ps1                                        # against retail
#   .\tools\verify-patch-targets.ps1 -GameDir "<path to the demo>"          # against the demo
# Build the mod against the SAME game first (-p:Managed=...\Managed), or the DLL under test will
# have been compiled against the other one.
#
# Expected as of 2026-08-10: Resolved 64, FAILED 0 -- on BOTH builds.
#
# ⚠ Run each game in a FRESH process (`powershell -NoProfile -File .\tools\...`). Both games ship an
# `Assembly-CSharp` with the same identity, so auditing the second one in a session that already
# loaded the first fails with "Assembly with same name is already loaded". That is a HOST
# limitation, not a finding -- do not read it as a broken build.
# ⚠ DEFAULTS TO THE NEWEST BUILD, not to a fixed configuration. This script silently audited a
# STALE bin\Debug DLL after a `dotnet build -c Release`, reporting an unchanged "Resolved: 64
# FAILED: 0" for a build that had just GAINED a hook. Green, wrong, and indistinguishable from a
# correct run - the newly added patch was simply absent from the listing. Picking whichever DLL is
# newer means the tool audits what was actually just built; pass -ModDll to override.
param(
  [string]$GameDir = "D:\SteamLibrary\steamapps\Common\Coffee Talk",
  [string]$ModDll
)
$ErrorActionPreference='Stop'
if(-not $ModDll){
  $candidates = @(
    "C:\Users\amock\Coffee Talk Access\bin\Release\CoffeeTalkAccess.dll",
    "C:\Users\amock\Coffee Talk Access\bin\Debug\CoffeeTalkAccess.dll"
  ) | Where-Object { Test-Path $_ } | Sort-Object { (Get-Item $_).LastWriteTime } -Descending
  if(-not $candidates){ throw "No built CoffeeTalkAccess.dll found in bin\Release or bin\Debug." }
  $ModDll = $candidates[0]
}
$managed = Join-Path $GameDir 'CoffeeTalk_Data\Managed'
# MelonLoader may not be installed in the game being audited (a fresh retail install has none), so
# fall back to any sibling install that does have it. It is only needed to satisfy the mod's
# references; it is never loaded successfully here (see the x86 note below).
$melon = Join-Path $GameDir 'MelonLoader\net35'
if(-not (Test-Path $melon)){
  $melon = "D:\SteamLibrary\steamapps\Common\Coffee Talk Demo\MelonLoader\net35"
}

$resolver = { param($s,$e)
  $sm=$e.Name.Split(',')[0]
  foreach($d in @($managed,$melon,"C:\Users\amock\Coffee Talk Access\bin\Debug")){
    $p=Join-Path $d "$sm.dll"; if(Test-Path $p){ return [System.Reflection.Assembly]::LoadFrom($p) }
  }
  return $null }
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)

$game = [System.Reflection.Assembly]::LoadFrom((Join-Path $managed 'Assembly-CSharp.dll'))
$mod  = [System.Reflection.Assembly]::LoadFrom($ModDll)
Write-Output "Game: $managed"
Write-Output "Mod:  $ModDll"

$B=[System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
$fail=0; $ok=0

# MelonLoader.dll is x86 and this host is x64, so it cannot be loaded here. That is expected and
# harmless: the ReflectionTypeLoadException still carries every type that DID load, which includes
# all the patch classes. Bail only if nothing came back.
# PowerShell WRAPS the .NET exception in a MethodInvocationException, so a typed catch never
# matches and $_.Exception.Types is null -- which silently yields ZERO types and a green
# "0 failed". Unwrap through InnerException until the real one is found.
$modTypes = $null
try { $modTypes = $mod.GetTypes() }
catch {
  $ex = $_.Exception
  while($ex -and -not ($ex -is [System.Reflection.ReflectionTypeLoadException])){ $ex = $ex.InnerException }
  if($ex){ $modTypes = @($ex.Types | Where-Object { $_ -ne $null }) }
}
$modTypes = @($modTypes)
if($modTypes.Count -eq 0){ throw "No mod types loaded - cannot verify." }
Write-Output "Mod types available: $($modTypes.Count)"

foreach($t in $modTypes){
  # class-level [HarmonyPatch(typeof(X))] contributes the declaring type for members below
  $classAttrs = $t.GetCustomAttributesData() | Where-Object { $_.AttributeType.Name -match '^HarmonyPatch(Attribute)?$' }
  $classType = $null; $classMethod = $null
  foreach($ca in $classAttrs){
    foreach($arg in $ca.ConstructorArguments){
      # ⚠ Match on the VALUE, not ArgumentType.Name: a Type-valued attribute argument reports its
      # ArgumentType as RuntimeType, so testing for 'Type' silently matches NOTHING and the whole
      # scan reports a green "0 failed" -- this script's own version of the failure it hunts for.
      if($arg.Value -is [Type]){ $classType = $arg.Value }
      elseif($arg.Value -is [string]){ $classMethod = $arg.Value }
    }
  }

  foreach($m in $t.GetMethods($B)){
    $attrs = $m.GetCustomAttributesData() | Where-Object { $_.AttributeType.Name -match '^HarmonyPatch(Attribute)?$' }
    if(-not $attrs){ continue }
    $tt = $classType; $mn = $classMethod
    foreach($a in $attrs){
      foreach($arg in $a.ConstructorArguments){
        if($arg.Value -is [Type]){ $tt = $arg.Value }
        elseif($arg.Value -is [string]){ $mn = $arg.Value }
      }
    }
    if(-not $tt -or -not $mn){ continue }

    # Re-resolve the target type against THIS game assembly by name.
    $gt = $game.GetTypes() | Where-Object { $_.FullName -eq $tt.FullName } | Select-Object -First 1
    if(-not $gt){
      # Fungus + other non-Assembly-CSharp targets: accept the type as given.
      $gt = $tt
    }
    $found = $gt.GetMethods([System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static,FlattenHierarchy') |
             Where-Object { $_.Name -eq $mn }
    if($found){
      $ok++
      $decl = ($found | Select-Object -First 1).DeclaringType.Name
      $note = if($decl -ne $gt.Name){ "  (inherited from $decl)" } else { '' }
      Write-Output ("  OK      {0}.{1} -> {2}.{3}{4}" -f $t.Name,$m.Name,$gt.Name,$mn,$note)
    } else {
      $fail++
      Write-Output ("  FAIL    {0}.{1} -> {2}.{3}  *** TARGET NOT FOUND ***" -f $t.Name,$m.Name,$gt.Name,$mn)
    }
  }
}
Write-Output ""
Write-Output "Resolved: $ok   FAILED: $fail"
