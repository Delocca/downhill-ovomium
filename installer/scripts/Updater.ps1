# Plugin de mise à jour automatique (Plugins\OvomiumUpdater.dll) et ses fichiers d'état (UserData\Ovomium\).
# Il ne compte pas comme « autre mod » : il ne doit pas retenir MelonLoader à la désinstallation.

$script:UpdaterName = 'OvomiumUpdater.dll'
# Mise à jour téléchargée en attente d'installation (au prochain lancement du jeu).
$script:UpdaterPending = @('update', 'update.tmp')
# Tout l'état du plugin (Core/Update/UpdateFiles.cs ; « .tmp » : écriture interrompue). Les enregistrements
# (UserData\Ovomium\recordings\) restent.
$script:UpdaterState = @('update', 'update.tmp', 'update.rejected', 'update-state.json', 'update-state.json.tmp',
    'installed.json', 'installed.json.tmp')

function Get-UpdaterStateDir([string]$GameDir) {
    return Join-Path (Join-Path $GameDir 'UserData') 'Ovomium'
}

# Copie le plugin dans Plugins\ et efface une mise à jour en attente : elle serait plus ancienne que ce paquet.
function Install-Updater([string]$GameDir, [string]$Payload) {
    $plugins = Join-Path $GameDir 'Plugins'
    if (-not (Test-Path -LiteralPath $plugins)) { [void](New-Item -ItemType Directory -Path $plugins) }
    $target = Join-Path $plugins $script:UpdaterName
    Copy-Item -LiteralPath (Join-Path $Payload $script:UpdaterName) -Destination $target -Force
    Write-Journal "Plugin de mise à jour copié : $target"
    $stateDir = Get-UpdaterStateDir $GameDir
    foreach ($name in $script:UpdaterPending) {
        $path = Join-Path $stateDir $name
        if (Test-Path -LiteralPath $path) {
            Write-Journal "Mise à jour en attente effacée (remplacée par cette installation) : $path"
            Remove-ToRecycleBin $path
        }
    }
}

# Retire le plugin et ses fichiers d'état ; UserData\Ovomium.cfg (réglages) reste.
function Uninstall-Updater([string]$GameDir) {
    $target = Join-Path (Join-Path $GameDir 'Plugins') $script:UpdaterName
    if (Test-Path -LiteralPath $target) { Remove-ToRecycleBin $target } else { Write-Journal "Plugin absent : $target" }
    $stateDir = Get-UpdaterStateDir $GameDir
    foreach ($name in $script:UpdaterState) { Remove-ToRecycleBin (Join-Path $stateDir $name) }
    if ((Test-Path -LiteralPath $stateDir) -and -not (Get-ChildItem -LiteralPath $stateDir -Force)) {
        Remove-Item -LiteralPath $stateDir -Force
    }
}
