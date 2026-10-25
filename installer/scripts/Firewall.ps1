# Pare-feu Windows : règle entrante UDP pour le jeu, profil réseau (Public bloque les autres joueurs).
# Lecture sans droits d'administrateur ; modifications dans Elevated.ps1, lancé via l'UAC.

$script:RuleGroup = 'LmdMulti'
$script:RuleName = 'LmdMulti - Lonely Mountains: Downhill (UDP entrant)'

function Test-IsAdmin {
    if (-not (Test-IsWindows)) { return $false }
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal $id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Règles entrantes qui visent ce programme (créées par nous ou par la question de Windows).
function Get-ProgramRules([string]$GameExe) {
    $filters = @(Get-NetFirewallApplicationFilter -Program $GameExe -ErrorAction SilentlyContinue)
    if ($filters.Count -eq 0) { return @() }
    return @($filters | Get-NetFirewallRule -ErrorAction SilentlyContinue |
        Where-Object { $_.Direction -eq 'Inbound' })
}

# Règles de blocage actives sur le profil Privé (ex. « Annuler » à la question de Windows).
function Get-BlockingRules([string]$GameExe) {
    return @(Get-ProgramRules $GameExe | Where-Object {
        $_.Action -eq 'Block' -and $_.Enabled -eq 'True' -and
        ("$($_.Profile)" -match 'Any|Private')
    })
}

# Vrai si notre règle existe, vise ce jeu, et qu'aucun blocage ne la contredit.
function Test-FirewallReady([string]$GameExe) {
    try {
        $ours = @(Get-NetFirewallRule -Group $script:RuleGroup -ErrorAction SilentlyContinue)
        if ($ours.Count -eq 0) { return $false }
        $programs = @($ours | Get-NetFirewallApplicationFilter | ForEach-Object { $_.Program })
        if ($programs -notcontains $GameExe) { return $false }
        return (Get-BlockingRules $GameExe).Count -eq 0
    } catch {
        Write-Journal "  lecture du pare-feu impossible : $($_.Exception.Message)"
        return $false
    }
}

# Connexions actives (réseau local ou Internet) en profil Public : @( @{ Index; Name } ).
function Get-PublicNetworks {
    try {
        return @(Get-NetConnectionProfile -ErrorAction Stop |
            Where-Object { "$($_.NetworkCategory)" -eq 'Public' -and
                "$($_.IPv4Connectivity)" -match 'LocalNetwork|Internet' } |
            ForEach-Object { @{ Index = $_.InterfaceIndex; Name = "$($_.Name) ($($_.InterfaceAlias))" } })
    } catch {
        Write-Journal "  profils réseau illisibles : $($_.Exception.Message)"
        return @()
    }
}

# --- Partie administrateur (appelée par Elevated.ps1) ---

function Add-GameRule([string]$GameExe) {
    Get-NetFirewallRule -Group $script:RuleGroup -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    foreach ($rule in Get-BlockingRules $GameExe) {
        Write-Journal "  règle de blocage retirée : $($rule.DisplayName) ($($rule.Profile))"
        $rule | Remove-NetFirewallRule
    }
    [void](New-NetFirewallRule -DisplayName $script:RuleName -Group $script:RuleGroup `
        -Direction Inbound -Action Allow -Program $GameExe -Protocol UDP -Profile Private, Domain `
        -Description 'Mod LmdMulti : voir les autres joueurs du réseau local (port 47700 et suivants).')
    Write-Journal "  règle ajoutée : $($script:RuleName) -> $GameExe"
}

function Remove-GameRule {
    $rules = @(Get-NetFirewallRule -Group $script:RuleGroup -ErrorAction SilentlyContinue)
    $rules | Remove-NetFirewallRule
    Write-Journal "  $($rules.Count) règle(s) LmdMulti retirée(s)"
}

# Échec non bloquant (certaines cartes virtuelles refusent) : la règle est déjà en place.
function Set-NetworksPrivate([int[]]$Indexes) {
    foreach ($i in $Indexes) {
        try {
            Set-NetConnectionProfile -InterfaceIndex $i -NetworkCategory Private -ErrorAction Stop
            Write-Journal "  réseau n°$i passé en Privé"
        } catch {
            Write-Journal "  réseau n°$i : passage en Privé impossible ($($_.Exception.Message))"
        }
    }
}

# --- Côté utilisateur : lance Elevated.ps1 (UAC). Vrai si l'étape a réussi. ---

function Invoke-Elevated([string]$Action, [string]$GameExe, [int[]]$MakePrivate) {
    $entry = Join-Path $PSScriptRoot 'Elevated.ps1'
    $private = ($MakePrivate | ForEach-Object { "$_" }) -join ','
    if (Test-IsAdmin) {
        & $entry -Action $Action -GameExe $GameExe -LogFile $script:LogPath -MakePrivate $private
        return $LASTEXITCODE -eq 0
    }
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$entry`" -Action $Action " +
        "-GameExe `"$GameExe`" -LogFile `"$($script:LogPath)`""
    if ($private) { $argList += " -MakePrivate $private" }
    try {
        $p = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ArgumentList $argList
        Write-Journal "  étape administrateur terminée (code $($p.ExitCode))"
        return $p.ExitCode -eq 0
    } catch {
        Write-Journal "  autorisation administrateur refusée ou impossible : $($_.Exception.Message)"
        return $false
    }
}
