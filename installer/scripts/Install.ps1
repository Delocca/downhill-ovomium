# Installeur Ovomium (lancé par « Installer Ovomium.bat »).
# Trouve le jeu, installe MelonLoader si besoin, copie le mod (et LMD-Camera), ouvre le pare-feu (UAC), journalise.
# Paramètres de test : -SteamRoot / -GameDir (pas de registre), -Answers "Clé=Oui,Clé2=Non" (sans fenêtres).
# Code de sortie : 0 réussi ; 21-24 annulé (expliqué à l'écran) ; 9 erreur ; 1 = PowerShell n'a pas pu lancer le script.
param(
    [string]$GameDir = '',
    [string]$SteamRoot = '',
    [string]$Answers = '',
    [string]$LogFile = ''
)

. (Join-Path $PSScriptRoot 'Common.ps1')
. (Join-Path $PSScriptRoot 'Steam.ps1')
. (Join-Path $PSScriptRoot 'Melon.ps1')
. (Join-Path $PSScriptRoot 'Firewall.ps1')
. (Join-Path $PSScriptRoot 'Camera.ps1')

$script:ModVersion = '@MOD_VERSION@'      # remplacés par tools/make-dist.sh
$script:TestedBuildId = '@TESTED_BUILD@'
$script:Payload = Split-Path -Parent $PSScriptRoot
$script:Notes = @()

function Install-ModDll([string]$Dir) {
    $mods = Join-Path $Dir 'Mods'
    if (-not (Test-Path -LiteralPath $mods)) { [void](New-Item -ItemType Directory -Path $mods) }
    $target = Join-Path $mods 'Ovomium.dll'
    Copy-Item -LiteralPath (Join-Path $script:Payload 'Ovomium.dll') -Destination $target -Force
    Write-Journal "Mod copié : $target"
    $cfg = Join-Path (Join-Path $Dir 'UserData') 'Ovomium.cfg'
    if (Test-Path -LiteralPath $cfg) { Write-Journal "Réglages existants gardés : $cfg" }
}

function Test-GameVersion([string]$BuildId) {
    if (-not $BuildId) { Write-Journal 'Version du jeu : inconnue (pas de manifeste Steam)'; return }
    Write-Journal "Version du jeu : build Steam $BuildId (testé : $($script:TestedBuildId))"
    if ($BuildId -ne $script:TestedBuildId) {
        $script:Notes += "• Le jeu a été mis à jour depuis la sortie de ce mod : s'il ne marche plus, " +
            'demandez une nouvelle version d''Ovomium.'
    }
}

# Propose de passer les connexions « Public » en « Privé ». Retourne leurs numéros si Oui.
function Read-MakePrivate {
    $public = @(Get-PublicNetworks)
    if ($public.Count -eq 0) { return @() }
    $names = ($public | ForEach-Object { "« $($_.Name) »" }) -join ', '
    $yes = Read-YesNo 'PasserEnPrive' ("Votre connexion $names est réglée sur « Réseau public » : " +
        "Windows y cache votre PC aux autres joueurs.`n`nLa passer en « Réseau privé » ?`n`n" +
        "Oui : recommandé à la maison ou chez des amis.`nNon : dans un lieu public (gare, café…).") $true
    if ($yes) { return @($public | ForEach-Object { $_.Index }) }
    $script:Notes += '• Réseau en « Public » : les autres joueurs ne vous verront pas sur ce réseau.'
    return @()
}

function Install-Firewall([string]$GameExe) {
    if (-not (Test-IsWindows)) { Write-Journal 'Pare-feu : ignoré (pas Windows)'; return }
    $private = @(Read-MakePrivate)
    if ($private.Count -eq 0 -and (Test-FirewallReady $GameExe)) {
        Write-Journal 'Pare-feu : règle Ovomium déjà en place'
        return
    }
    Write-Host ''
    Write-Host '>>> Windows va demander une autorisation (pare-feu) : cliquez sur « Oui ». <<<' -ForegroundColor Yellow
    if (Invoke-Elevated 'Add' $GameExe $private) { return }
    $script:Notes += "• Pare-feu non réglé (autorisation refusée). Au premier lancement, si Windows " +
        "demande d'autoriser le jeu : cochez « Réseaux privés » puis « Autoriser l'accès ». " +
        "Ou relancez l'installeur et répondez Oui."
}

function Get-MelonNote([string]$Status) {
    switch ($Status) {
        'gardé' { return "• Vous avez gardé votre MelonLoader : Ovomium est prévu pour la version fournie." }
        'à jour' { return '' }
        default { return "• Premier lancement LONG (2 à 10 minutes) : MelonLoader prépare le jeu dans une " +
            "fenêtre noire. C'est normal, ne fermez rien ; le jeu s'ouvre ensuite." }
    }
}

function Show-Success([string]$Dir, [string]$MelonStatus) {
    $note = Get-MelonNote $MelonStatus
    if ($note) { $script:Notes = @($note) + $script:Notes }
    $text = "Ovomium $($script:ModVersion) est installé !`n`nDossier du jeu : $Dir`nMelonLoader : $MelonStatus`n`n" +
        "Lancez le jeu depuis Steam, comme d'habitude.`n"
    if ($script:Notes.Count -gt 0) { $text += "`n" + ($script:Notes -join "`n`n") + "`n" }
    $text += "`nEn jeu : F3 = joueurs connectés, F1 = lancer / rejoindre une course " +
        "(manette : Select + A). Voir LISEZ-MOI.txt.`n`nJournal : $($script:LogPath)"
    Show-Message $text 'Info'
}

function Invoke-Install {
    $files = @('Ovomium.dll', $script:CameraName) | ForEach-Object { Join-Path $script:Payload $_ }
    if (@($files | Where-Object { -not (Test-Path -LiteralPath $_) }).Count -gt 0) {
        Show-Message "Fichiers de l'installeur incomplets : extrayez tout le zip puis relancez." 'Error'
        return 21
    }
    $game = Resolve-Game $GameDir $SteamRoot
    if (-not $game) { Show-Message 'Installation annulée : jeu introuvable.' 'Error'; return 22 }
    Write-Journal "Jeu : $($game.Dir)"
    if (-not (Wait-GameClosed)) { Show-Message 'Installation annulée : le jeu est ouvert.' 'Warning'; return 23 }
    Test-GameVersion $game.BuildId
    $melon = Install-MelonLoader $game.Dir (Join-Path $script:Payload 'melonloader')
    Write-Journal "MelonLoader : $melon"
    if ($melon -eq 'abandon') { Show-Message 'Installation annulée : rien n''a été modifié.' 'Warning'; return 24 }
    Install-ModDll $game.Dir
    Install-CameraMod $game.Dir $script:Payload
    Clear-DownloadMark (Join-Path $game.Dir 'Mods')
    Clear-DownloadMark (Join-Path $game.Dir 'MelonLoader')
    Clear-DownloadMark (Join-Path $game.Dir 'version.dll')
    Install-Firewall (Join-Path $game.Dir $script:GameExeName)
    Show-Success $game.Dir $melon
    return 0
}

if (-not $LogFile) { $LogFile = Get-DefaultLogFile 'Ovomium-installation.log' }
Initialize-Session $LogFile $Answers
$ErrorActionPreference = 'Stop'
try {
    $code = Invoke-Install
} catch {
    Write-Journal "ERREUR : $($_.Exception.Message)`n$($_.ScriptStackTrace)"
    Show-Message ("L'installation a échoué :`n$($_.Exception.Message)`n`n" +
        "Si le message parle d'accès refusé : clic droit sur « Installer Ovomium.bat » → " +
        "« Exécuter en tant qu'administrateur ».`n`n" +
        "Sinon, envoyez le journal à la personne qui vous a donné le mod :`n$($script:LogPath)") 'Error'
    $code = 9
}
Write-Journal "Fin (code $code)"
exit $code
