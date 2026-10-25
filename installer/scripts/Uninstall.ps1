# Désinstalleur Ovomium (lancé par « Desinstaller Ovomium.bat »).
# Retire le mod, LMD-Camera (si c'est la copie livrée) et la règle de pare-feu ;
# propose de retirer MelonLoader s'il n'y a pas d'autre mod.
# Réglages (UserData) gardés. Mêmes paramètres de test que Install.ps1.
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

$script:Payload = Split-Path -Parent $PSScriptRoot
$script:Notes = @()

function Uninstall-Firewall {
    if (-not (Test-IsWindows)) { Write-Journal 'Pare-feu : ignoré (pas Windows)'; return }
    $rules = @(Get-NetFirewallRule -Group $script:RuleGroup -ErrorAction SilentlyContinue)
    if ($rules.Count -eq 0) { Write-Journal 'Pare-feu : aucune règle Ovomium'; return }
    Write-Host ''
    Write-Host '>>> Windows va demander une autorisation (pare-feu) : cliquez sur « Oui ». <<<' -ForegroundColor Yellow
    if (-not (Invoke-Elevated 'Remove' '' @())) {
        $script:Notes += "• La règle de pare-feu « $($script:RuleName) » est restée (autorisation refusée). Sans le mod, elle ne sert à rien."
    }
}

# Retire MelonLoader si aucun autre mod ne s'en sert (et si l'utilisateur est d'accord).
function Uninstall-MelonIfUnused([string]$Dir) {
    $state = Get-MelonState $Dir
    if (-not $state.Present -or $state.Foreign) { return 'absent' }
    $others = Get-OtherMods $Dir
    if ($others.Count -gt 0) {
        Write-Journal "Autres mods : $($others -join ', ')"
        $script:Notes += "• MelonLoader gardé : d'autres mods s'en servent ($($others -join ', '))."
        return 'gardé'
    }
    $remove = Read-YesNo 'RetirerMelonLoader' ("Aucun autre mod n'utilise MelonLoader (le chargeur de mods).`n`n" +
        "Le retirer aussi ? (il va à la corbeille ; le jeu redevient d'origine)`n`nRecommandé : Oui.") $true
    if (-not $remove) { return 'gardé' }
    Uninstall-MelonLoader $Dir
    return 'retiré'
}

function Invoke-Uninstall {
    $game = Resolve-Game $GameDir $SteamRoot
    if (-not $game) { Show-Message 'Désinstallation annulée : jeu introuvable.' 'Error'; return 22 }
    Write-Journal "Jeu : $($game.Dir)"
    if (-not (Wait-GameClosed)) { Show-Message 'Désinstallation annulée : le jeu est ouvert.' 'Warning'; return 23 }
    $dll = Join-Path (Join-Path $game.Dir 'Mods') 'Ovomium.dll'
    if (Test-Path -LiteralPath $dll) { Remove-ToRecycleBin $dll } else { Write-Journal "Mod absent : $dll" }
    # Avant le test « autres mods » : notre copie de LMD-Camera ne doit pas retenir MelonLoader.
    $camera = Uninstall-CameraMod $game.Dir $script:Payload
    Write-Journal "LMD-Camera : $camera"
    Uninstall-Firewall
    $melon = Uninstall-MelonIfUnused $game.Dir
    Write-Journal "MelonLoader : $melon"
    $userData = Join-Path $game.Dir 'UserData'
    if (Test-Path -LiteralPath (Join-Path $userData 'Ovomium.cfg')) {
        $script:Notes += "• Vos réglages sont gardés dans $userData (Ovomium.cfg). Supprimez-les si vous n'en voulez plus."
    }
    $text = "Ovomium est désinstallé.`n`nMelonLoader : $melon"
    if ($script:Notes.Count -gt 0) { $text += "`n`n" + ($script:Notes -join "`n`n") }
    Show-Message "$text`n`nJournal : $($script:LogPath)" 'Info'
    return 0
}

if (-not $LogFile) { $LogFile = Get-DefaultLogFile 'Ovomium-desinstallation.log' }
Initialize-Session $LogFile $Answers
$ErrorActionPreference = 'Stop'
try {
    $code = Invoke-Uninstall
} catch {
    Write-Journal "ERREUR : $($_.Exception.Message)`n$($_.ScriptStackTrace)"
    Show-Message ("La désinstallation a échoué :`n$($_.Exception.Message)`n`nJournal :`n$($script:LogPath)") 'Error'
    $code = 9
}
Write-Journal "Fin (code $code)"
exit $code
