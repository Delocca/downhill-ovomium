# Scénarios de l'installeur PowerShell, joués sous pwsh (Linux) sur une fausse arborescence Steam.
# Install.ps1 / Uninstall.ps1 tournent sans fenêtres (-Answers) ; pare-feu ignoré hors Windows.
param(
    [Parameter(Mandatory = $true)][string]$Dist,   # dossier extrait du zip
    [Parameter(Mandatory = $true)][string]$Work    # contient MelonLoader-0.6.1.dll, MelonLoader-0.8.0.dll
)
$ErrorActionPreference = 'Stop'
$script:Failures = 0
$scripts = Join-Path $Dist 'fichiers/scripts'
$payloadMelon = Join-Path $Dist 'fichiers/melonloader'
$camera = Join-Path $Dist 'fichiers/LMD-Camera.dll'

function Check([bool]$Condition, [string]$Message) {
    if ($Condition) { Write-Host "OK   $Message" } else { Write-Host "ÉCHEC $Message"; $script:Failures++ }
}

function Write-Text([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    [IO.File]::WriteAllText($Path, $Text)
}

# Steam avec deux bibliothèques ; le jeu dans la seconde (chemin avec espaces et accents).
function New-FakeSteam([string]$Case, [bool]$WithGame = $true, [string]$BuildId = '12535418') {
    $base = Join-Path $Work "cases/$Case"
    $steam = Join-Path $base 'Steam'
    $lib2 = Join-Path $base 'Bibliothèque Jeux 2'
    $vdf = "`"libraryfolders`"`n{`n`t`"0`"`n`t{`n`t`t`"path`"`t`t`"$steam`"`n`t}`n" +
        "`t`"1`"`n`t{`n`t`t`"path`"`t`t`"$lib2`"`n`t`t`"apps`"`n`t`t{`n`t`t`t`"711540`"`t`t`"1`"`n`t`t}`n`t}`n}`n"
    Write-Text (Join-Path $steam 'steamapps/libraryfolders.vdf') $vdf
    $game = Join-Path $lib2 'steamapps/common/Lonely Mountains - Downhill'
    if ($WithGame) {
        Write-Text (Join-Path $lib2 'steamapps/appmanifest_711540.acf') ("`"AppState`"`n{`n`t`"appid`"`t`t`"711540`"`n" +
            "`t`"installdir`"`t`t`"Lonely Mountains - Downhill`"`n`t`"buildid`"`t`t`"$BuildId`"`n}`n")
        Write-Text (Join-Path $game 'LMD_Win_x64.exe') 'exe'
    }
    return @{ Steam = $steam; Game = $game; Log = (Join-Path $base 'install.log') }
}

function Invoke-Installer([string]$Script, $Env, [string]$Answers = 'Aucune=Non', [string]$GameDir = '') {
    $argList = @('-NoProfile', '-File', (Join-Path $scripts $Script), '-SteamRoot', $Env.Steam,
        '-LogFile', $Env.Log, '-Answers', $Answers)
    if ($GameDir) { $argList += @('-GameDir', $GameDir) }
    & pwsh @argList *> $null
    return $LASTEXITCODE
}

function Get-Log($Env) { return Get-Content -LiteralPath $Env.Log -Raw }
function Test-Same([string]$A, [string]$B) { return (Get-FileHash $A).Hash -eq (Get-FileHash $B).Hash }
function P([string]$Dir, [string]$Rel) { return Join-Path $Dir $Rel }

# Ancien MelonLoader de version $Version (+ fichier témoin pour vérifier son retrait).
function Set-OldMelon([string]$Game, [string]$Version) {
    New-Item -ItemType Directory -Force -Path (P $Game 'MelonLoader/net6') | Out-Null
    Copy-Item (Join-Path $Work "MelonLoader-$Version.dll") (P $Game 'MelonLoader/net6/MelonLoader.dll')
    Write-Text (P $Game 'MelonLoader/ancien-temoin.txt') $Version
    Write-Text (P $Game 'version.dll') "ancien proxy $Version"
}

function Test-FreshInstall {
    $e = New-FakeSteam 'neuf'
    Write-Text (P $e.Game 'UserData/Ovomium.cfg') "Port = 47701`nNickname = `"Edia`"`n"
    $code = Invoke-Installer 'Install.ps1' $e
    Check ($code -eq 0) "neuf : code 0 (obtenu $code)"
    Check (Test-Same (P $e.Game 'Mods/Ovomium.dll') (P $Dist 'fichiers/Ovomium.dll')) 'neuf : DLL copiée dans Mods'
    Check (Test-Same (P $e.Game 'Mods/LMD-Camera.dll') $camera) 'neuf : LMD-Camera copié dans Mods'
    Check (Test-Same (P $e.Game 'Plugins/OvomiumUpdater.dll') (P $Dist 'fichiers/OvomiumUpdater.dll')) 'neuf : plugin de mise à jour dans Plugins'
    Check (Test-Same (P $e.Game 'version.dll') (P $payloadMelon 'version.dll')) 'neuf : version.dll de MelonLoader'
    Check (Test-Path (P $e.Game 'MelonLoader/net6/MelonLoader.dll')) 'neuf : dossier MelonLoader'
    Check ((Get-Content (P $e.Game 'UserData/Ovomium.cfg') -Raw) -match 'Port = 47701') 'neuf : config existante intacte'
    $log = Get-Log $e
    Check ($log -match 'Bibliothèque Jeux 2') 'neuf : jeu trouvé dans la 2e bibliothèque'
    Check ($log -match 'MelonLoader : installé') 'neuf : MelonLoader installé'
    Check ($log -match 'Réglages existants gardés') 'neuf : config signalée gardée'
    Check ($log -notmatch 'mis à jour depuis') 'neuf : build testé, pas d''avertissement'
    Check ($log -match 'Ovomium 0\.\d+\.\d+ est installé') 'neuf : message final avec la version'

    Write-Text (P $e.Game 'UserData/Ovomium/update/Ovomium.dll') 'ancienne'
    Write-Text (P $e.Game 'UserData/Ovomium/update.tmp/x') 'partiel'
    Write-Text (P $e.Game 'UserData/Ovomium/installed.json') '{}'
    $code = Invoke-Installer 'Install.ps1' $e
    Check ($code -eq 0 -and ((Get-Log $e) -match 'MelonLoader : à jour')) 'réinstallation : MelonLoader déjà à jour'
    Check ((Get-Log $e) -match 'LMD-Camera copié : .*\(déjà là, identique\)') 'réinstallation : LMD-Camera identique, signalé'
    Check (-not (Test-Path (P $e.Game 'UserData/Ovomium/update')) -and -not (Test-Path (P $e.Game 'UserData/Ovomium/update.tmp')) -and
        (Test-Path (P $e.Game 'UserData/Ovomium/installed.json')) -and ((Get-Log $e) -match 'en attente effacée')) 'réinstallation : mise à jour en attente effacée, état gardé'
    return $e
}

function Test-NotFound {
    $e = New-FakeSteam 'introuvable' $false
    $code = Invoke-Installer 'Install.ps1' $e
    Check ($code -eq 22) "introuvable : code 22 (obtenu $code)"
    Check (-not (Test-Path (P $e.Game 'Mods'))) 'introuvable : rien d''écrit'
    $manual = Join-Path $Work 'cases/manuel/Mon jeu'
    Write-Text (P $manual 'LMD_Win_x64.exe') 'exe'
    $code = Invoke-Installer 'Install.ps1' $e "Dossier=$manual"
    Check ($code -eq 0 -and (Test-Path (P $manual 'Mods/Ovomium.dll'))) 'introuvable : dossier choisi à la main'
    $code = Invoke-Installer 'Install.ps1' $e "Dossier=$Work"
    Check ($code -eq 22) 'introuvable : dossier sans LMD_Win_x64.exe refusé'
}

function Test-OtherMelon {
    $e = New-FakeSteam 'ancien-oui'
    Set-OldMelon $e.Game '0.6.1'
    Write-Text (P $e.Game 'Mods/AutreMod.dll') 'autre'
    $code = Invoke-Installer 'Install.ps1' $e 'RemplacerMelonLoader=Oui'
    Check ($code -eq 0 -and ((Get-Log $e) -match 'MelonLoader : mis à jour')) 'ancien 0.6.1 + Oui : mis à jour'
    Check (Test-Same (P $e.Game 'version.dll') (P $payloadMelon 'version.dll')) 'ancien + Oui : nouveau version.dll'
    Check (-not (Test-Path (P $e.Game 'MelonLoader/ancien-temoin.txt'))) 'ancien + Oui : ancien dossier retiré'
    Check ((Get-Log $e) -match '1 autre\(s\) mod') 'ancien + Oui : autres mods signalés'
    Check (Test-Path (P $e.Game 'Mods/AutreMod.dll')) 'ancien + Oui : autre mod intact'

    $e = New-FakeSteam 'ancien-defaut'
    Set-OldMelon $e.Game '0.6.1'
    [void](Invoke-Installer 'Install.ps1' $e)
    Check ((Get-Log $e) -match 'RemplacerMelonLoader\].*-> Oui') 'ancien sans réponse : Oui par défaut'

    $e = New-FakeSteam 'recent'
    Set-OldMelon $e.Game '0.8.0'
    $code = Invoke-Installer 'Install.ps1' $e
    Check ($code -eq 0 -and ((Get-Log $e) -match 'MelonLoader : gardé')) 'plus récent 0.8.0 sans réponse : gardé'
    Check ((Get-Content (P $e.Game 'version.dll') -Raw) -match 'ancien proxy') 'plus récent : version.dll intact'
    Check (Test-Path (P $e.Game 'Mods/Ovomium.dll')) 'plus récent : mod quand même copié'
}

function Test-ForeignAndRepair {
    $e = New-FakeSteam 'etranger'
    Write-Text (P $e.Game 'version.dll') 'autre outil'
    $code = Invoke-Installer 'Install.ps1' $e
    Check ($code -eq 24) "version.dll étranger, sans réponse : abandon code 24 (obtenu $code)"
    Check (-not (Test-Path (P $e.Game 'Mods'))) 'étranger : rien modifié'
    $code = Invoke-Installer 'Install.ps1' $e 'RemplacerVersionDll=Oui'
    Check ($code -eq 0 -and (Test-Path (P $e.Game 'MelonLoader'))) 'étranger + Oui : installé'

    $e = New-FakeSteam 'incomplet'
    Copy-Item -Recurse (P $payloadMelon 'MelonLoader') (P $e.Game 'MelonLoader')
    [void](Invoke-Installer 'Install.ps1' $e)
    Check (((Get-Log $e) -match 'MelonLoader : réparé') -and (Test-Path (P $e.Game 'version.dll'))) 'même version sans version.dll : réparé'

    $e = New-FakeSteam 'autre-build' $true '99999999'
    [void](Invoke-Installer 'Install.ps1' $e)
    Check ((Get-Log $e) -match 'mis à jour depuis la sortie') 'autre build du jeu : avertissement'
}

function Test-Uninstall($Installed) {
    $e = $Installed
    Write-Text (P $e.Game 'UserData/Ovomium/update-state.json') '{}'
    Write-Text (P $e.Game 'UserData/Ovomium/update/Ovomium.dll') 'en attente'
    Write-Text (P $e.Game 'UserData/Ovomium/update.rejected/Ovomium.dll') 'refusée'
    Write-Text (P $e.Game 'UserData/Ovomium/installed.json.tmp') '{}'
    Write-Text (P $e.Game 'UserData/Ovomium/update-skip.json') '{}'
    Write-Text (P $e.Game 'UserData/Ovomium/recordings/r.bin') 'x'
    $code = Invoke-Installer 'Uninstall.ps1' $e 'RetirerMelonLoader=Non'
    Check ($code -eq 0 -and -not (Test-Path (P $e.Game 'Mods/Ovomium.dll'))) 'désinstallation : DLL retirée'
    Check (-not (Test-Path (P $e.Game 'Mods/LMD-Camera.dll'))) 'désinstallation : LMD-Camera livré retiré'
    $left = @(Get-ChildItem -LiteralPath (P $e.Game 'UserData/Ovomium') -Force | ForEach-Object Name)
    Check (-not (Test-Path (P $e.Game 'Plugins/OvomiumUpdater.dll')) -and ($left -join ',') -eq 'recordings' -and
        (Test-Path (P $e.Game 'UserData/Ovomium.cfg'))) 'désinstallation : plugin et état de mise à jour (rejected, .tmp) retirés, réglages et enregistrements gardés'
    Remove-Item -LiteralPath (P $e.Game 'UserData/Ovomium') -Recurse -Force
    Check (Test-Path (P $e.Game 'version.dll')) 'désinstallation + Non : MelonLoader gardé'
    [void](Invoke-Installer 'Install.ps1' $e)
    $code = Invoke-Installer 'Uninstall.ps1' $e
    Check ((Get-Log $e) -match 'Question RetirerMelonLoader\].*-> Oui') 'désinstallation : LMD-Camera et plugin livrés ne comptent pas comme autres mods'
    Check ($code -eq 0 -and -not (Test-Path (P $e.Game 'version.dll')) -and -not (Test-Path (P $e.Game 'MelonLoader'))) 'désinstallation (défaut Oui) : MelonLoader retiré'
    Check (-not (Test-Path (P $e.Game 'Mods')) -and -not (Test-Path (P $e.Game 'Plugins'))) 'désinstallation : dossiers Mods et Plugins vides retirés'
    Check (Test-Path (P $e.Game 'UserData/Ovomium.cfg')) 'désinstallation : réglages gardés'
    Check (Test-Path (P $e.Game 'LMD_Win_x64.exe')) 'désinstallation : jeu intact'

    $e = New-FakeSteam 'desinst-autre'
    [void](Invoke-Installer 'Install.ps1' $e)
    Write-Text (P $e.Game 'Mods/AutreMod.dll') 'autre'
    [void](Invoke-Installer 'Uninstall.ps1' $e 'RetirerMelonLoader=Oui')
    Check ((Test-Path (P $e.Game 'version.dll')) -and (Test-Path (P $e.Game 'Mods/AutreMod.dll'))) 'désinstallation avec autre mod : MelonLoader gardé sans question'
    Check ((Get-Log $e) -notmatch 'Question RetirerMelonLoader') 'désinstallation avec autre mod : aucune question'
}

# LMD-Camera déjà présent : écrasé à l'installation ; à la désinstallation, gardé seulement s'il diffère de la copie livrée.
function Test-CameraOwnership {
    $e = New-FakeSteam 'camera-ancienne'
    Write-Text (P $e.Game 'Mods/LMD-Camera.dll') 'ancienne camera'
    $code = Invoke-Installer 'Install.ps1' $e
    Check ($code -eq 0 -and (Test-Same (P $e.Game 'Mods/LMD-Camera.dll') $camera)) 'LMD-Camera existant : remplacé par la version livrée'
    Check ((Get-Log $e) -match 'remplace une autre version') 'LMD-Camera existant : remplacement journalisé'

    $e = New-FakeSteam 'camera-joueur'
    [void](Invoke-Installer 'Install.ps1' $e)
    Write-Text (P $e.Game 'Mods/LMD-Camera.dll') 'camera du joueur'
    $code = Invoke-Installer 'Uninstall.ps1' $e 'RetirerMelonLoader=Oui'
    Check ($code -eq 0 -and (Test-Path (P $e.Game 'Mods/LMD-Camera.dll'))) 'désinstallation, LMD-Camera différent : gardé'
    Check (-not (Test-Path (P $e.Game 'Mods/Ovomium.dll'))) 'désinstallation, LMD-Camera différent : Ovomium retiré quand même'
    $log = Get-Log $e
    Check ($log -match 'LMD-Camera gardé' -and $log -match 'installé vous-même') 'désinstallation, LMD-Camera différent : dit dans le journal et le message'
    Check ((Test-Path (P $e.Game 'version.dll')) -and $log -notmatch 'Question RetirerMelonLoader') 'désinstallation, LMD-Camera différent : MelonLoader gardé sans question'
}

function Test-VdfParsing {
    . (Join-Path $scripts 'Common.ps1')
    . (Join-Path $scripts 'Steam.ps1')
    $new = "`"libraryfolders`"`n{`n`"0`"`n{`n`"path`"`t`t`"C:\\Program Files (x86)\\Steam`"`n}`n`"1`"`n{`n`"path`"`t`"D:\\Jeux Vidéo\\SteamLibrary`"`n}`n}"
    $paths = @(Get-LibraryPaths $new)
    Check ($paths.Count -eq 2 -and $paths[1] -eq 'D:\Jeux Vidéo\SteamLibrary') 'VDF récent : chemins Windows déséchappés'
    $old = "`"LibraryFolders`"`n{`n`"TimeNextStatsReport`"`t`"123`"`n`"1`"`t`"E:\\SteamLibrary`"`n}"
    $paths = @(Get-LibraryPaths $old)
    Check ($paths.Count -eq 1 -and $paths[0] -eq 'E:\SteamLibrary') 'VDF ancien format ("1" "chemin")'
}

$installed = Test-FreshInstall
Test-NotFound
Test-OtherMelon
Test-ForeignAndRepair
Test-Uninstall $installed
Test-CameraOwnership
Test-VdfParsing
if ($script:Failures -gt 0) { Write-Host "$($script:Failures) échec(s)"; exit 1 }
Write-Host 'Installeur PowerShell : tous les scénarios passent'
