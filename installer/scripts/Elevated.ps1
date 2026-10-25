# Étape administrateur (lancée via l'UAC par Install.ps1 / Uninstall.ps1) : pare-feu et profil réseau.
# Code de sortie 0 = réussi. Écrit dans le même journal que l'installeur.
param(
    [Parameter(Mandatory = $true)][ValidateSet('Add', 'Remove')][string]$Action,
    [string]$GameExe = '',
    [string]$LogFile = '',
    [string]$MakePrivate = ''
)

. (Join-Path $PSScriptRoot 'Common.ps1')
. (Join-Path $PSScriptRoot 'Firewall.ps1')
$script:LogPath = $LogFile
$script:Interactive = $false
$ErrorActionPreference = 'Stop'

try {
    Write-Journal "Étape administrateur : $Action"
    if ($Action -eq 'Add') {
        Add-GameRule $GameExe
        $indexes = @($MakePrivate -split ',' | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ })
        Set-NetworksPrivate $indexes
    } else {
        Remove-GameRule
    }
    exit 0
} catch {
    Write-Journal "  ÉCHEC de l'étape administrateur : $($_.Exception.Message)"
    exit 1
}
