# Mod LMD-Camera de flo-st (vue à la première personne, licence MIT), livré tel quel avec Ovomium.
# Désinstallé seulement si c'est la copie livrée (même SHA-256 que fichiers\LMD-Camera.dll) :
# une autre version vient du joueur lui-même, on la garde.

$script:CameraName = 'LMD-Camera.dll'

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-CameraTarget([string]$GameDir) {
    return Join-Path (Join-Path $GameDir 'Mods') $script:CameraName
}

# Copie Mods\LMD-Camera.dll (écrase une version existante). Le dossier Mods existe déjà.
function Install-CameraMod([string]$GameDir, [string]$Payload) {
    $source = Join-Path $Payload $script:CameraName
    $target = Get-CameraTarget $GameDir
    $detail = ''
    if (Test-Path -LiteralPath $target) {
        $have = Get-Sha256 $target
        $detail = " (remplace une autre version, SHA-256 $have)"
        if ($have -eq (Get-Sha256 $source)) { $detail = ' (déjà là, identique)' }
    }
    Copy-Item -LiteralPath $source -Destination $target -Force
    Write-Journal "Mod LMD-Camera copié : $target$detail"
}

# Retire Mods\LMD-Camera.dll si c'est la copie livrée. Retourne : retiré, gardé, absent.
function Uninstall-CameraMod([string]$GameDir, [string]$Payload) {
    $target = Get-CameraTarget $GameDir
    if (-not (Test-Path -LiteralPath $target)) { Write-Journal "LMD-Camera absent : $target"; return 'absent' }
    $source = Join-Path $Payload $script:CameraName
    if (-not (Test-Path -LiteralPath $source)) {
        Write-Journal "LMD-Camera gardé : copie de référence absente ($source), impossible de vérifier"
        return 'gardé'
    }
    $have = Get-Sha256 $target
    if ($have -eq (Get-Sha256 $source)) { Remove-ToRecycleBin $target; return 'retiré' }
    Write-Journal "LMD-Camera gardé : $target n'est pas la version livrée avec Ovomium (SHA-256 $have)"
    $script:Notes += "• Mods\$($script:CameraName) gardé : ce n'est pas la copie installée par Ovomium " +
        '(vous l''aviez installé vous-même). Supprimez-le si vous n''en voulez plus.'
    return 'gardé'
}
