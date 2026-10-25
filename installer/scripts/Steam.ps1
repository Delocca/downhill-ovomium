# Recherche de Lonely Mountains: Downhill dans les bibliothèques Steam.
# Steam → bibliothèques (libraryfolders.vdf) → appmanifest_711540.acf → dossier du jeu.

$script:AppId = '711540'
$script:GameExeName = 'LMD_Win_x64.exe'
$script:DefaultInstallDir = 'Lonely Mountains - Downhill'

# Dossiers d'installation de Steam connus du registre (+ emplacement par défaut).
function Get-SteamRoots {
    $roots = @()
    $keys = @(
        @{ Path = 'HKCU:\Software\Valve\Steam'; Name = 'SteamPath' },
        @{ Path = 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam'; Name = 'InstallPath' },
        @{ Path = 'HKLM:\SOFTWARE\Valve\Steam'; Name = 'InstallPath' }
    )
    foreach ($k in $keys) {
        try {
            $value = (Get-ItemProperty -LiteralPath $k.Path -Name $k.Name -ErrorAction Stop).($k.Name)
            if ($value) { $roots += ($value -replace '/', '\') }
        } catch { }
    }
    if (${env:ProgramFiles(x86)}) { $roots += Join-Path ${env:ProgramFiles(x86)} 'Steam' }
    return $roots
}

# Valeurs « "clé" "valeur" » d'un fichier VDF/ACF de Steam, pour une clé donnée.
function Get-VdfValues([string]$Text, [string]$Key) {
    $pattern = '(?m)^\s*"' + [regex]::Escape($Key) + '"[ \t]+"((?:[^"\\]|\\.)*)"'
    foreach ($m in [regex]::Matches($Text, $pattern, 'IgnoreCase')) {
        $m.Groups[1].Value -replace '\\\\', '\'
    }
}

# Bibliothèques listées par libraryfolders.vdf (format récent "path", ou ancien "1" "D:\\...").
function Get-LibraryPaths([string]$VdfText) {
    $paths = @(Get-VdfValues $VdfText 'path')
    if ($paths.Count -eq 0) {
        foreach ($m in [regex]::Matches($VdfText, '(?m)^\s*"\d+"[ \t]+"((?:[^"\\]|\\.)*)"')) {
            $paths += $m.Groups[1].Value -replace '\\\\', '\'
        }
    }
    return $paths
}

function Get-SteamLibraries([string[]]$Roots) {
    $seen = @{}
    $libraries = @()
    foreach ($root in $Roots) {
        $candidates = @($root)
        $vdf = Join-Path (Join-Path $root 'steamapps') 'libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            $candidates += Get-LibraryPaths (Get-Content -LiteralPath $vdf -Raw -Encoding UTF8)
        }
        foreach ($lib in $candidates) {
            $norm = $lib.TrimEnd('\', '/').ToLowerInvariant()
            if ($norm -and -not $seen.ContainsKey($norm) -and (Test-Path -LiteralPath $lib)) {
                $seen[$norm] = $true
                $libraries += $lib
            }
        }
    }
    return $libraries
}

# Le jeu dans une bibliothèque : @{ Dir; BuildId } ou $null.
function Find-GameInLibrary([string]$Library) {
    $steamapps = Join-Path $Library 'steamapps'
    $acf = Join-Path $steamapps "appmanifest_$($script:AppId).acf"
    $installDir = $script:DefaultInstallDir
    $buildId = ''
    if (Test-Path -LiteralPath $acf) {
        $text = Get-Content -LiteralPath $acf -Raw -Encoding UTF8
        $dirValue = @(Get-VdfValues $text 'installdir')
        if ($dirValue.Count -gt 0) { $installDir = $dirValue[0] }
        $buildValue = @(Get-VdfValues $text 'buildid')
        if ($buildValue.Count -gt 0) { $buildId = $buildValue[0] }
    }
    $dir = Join-Path (Join-Path $steamapps 'common') $installDir
    if (Test-GameDir $dir) { return @{ Dir = $dir; BuildId = $buildId } }
    return $null
}

function Test-GameDir([string]$Dir) {
    return $Dir -and (Test-Path -LiteralPath (Join-Path $Dir $script:GameExeName) -PathType Leaf)
}

# Cherche le jeu : $SteamRoot (tests) ou registre. Retourne @{ Dir; BuildId } ou $null.
function Find-Game([string]$SteamRoot) {
    $roots = if ($SteamRoot) { @($SteamRoot) } else { Get-SteamRoots }
    Write-Journal "Steam : $($roots -join ' ; ')"
    $libraries = Get-SteamLibraries $roots
    Write-Journal "Bibliothèques : $($libraries -join ' ; ')"
    foreach ($lib in $libraries) {
        $game = Find-GameInLibrary $lib
        if ($game) { return $game }
    }
    return $null
}

# Demande le dossier du jeu jusqu'à en obtenir un valide. Retourne @{ Dir; BuildId } ou $null.
function Read-GameDir {
    $text = "Je ne trouve pas Lonely Mountains: Downhill.`n`n" +
        "Choisissez son dossier (celui qui contient $($script:GameExeName)).`n" +
        "Astuce : dans Steam, clic droit sur le jeu → Gérer → Parcourir les fichiers locaux."
    Show-Message $text 'Warning'
    while ($true) {
        $dir = Read-Folder "Dossier de Lonely Mountains: Downhill (contient $($script:GameExeName))"
        if (-not $dir) { return $null }
        if (Test-GameDir $dir) { return @{ Dir = $dir; BuildId = '' } }
        Write-Journal "Dossier refusé (pas de $($script:GameExeName)) : $dir"
        if (-not $script:Interactive) { return $null }
        Show-Message "Ce dossier ne contient pas $($script:GameExeName). Réessayez." 'Warning'
    }
}

function Resolve-Game([string]$GameDir, [string]$SteamRoot) {
    if ($GameDir) {
        if (Test-GameDir $GameDir) { return @{ Dir = $GameDir; BuildId = '' } }
        Write-Journal "Dossier donné invalide : $GameDir"
    } else {
        $game = Find-Game $SteamRoot
        if ($game) { return $game }
    }
    return Read-GameDir
}

# Vrai si le jeu tourne. Propose de réessayer après l'avoir fermé.
function Wait-GameClosed {
    $name = [IO.Path]::GetFileNameWithoutExtension($script:GameExeName)
    while (Get-Process -Name $name -ErrorAction SilentlyContinue) {
        $retry = Read-Retry 'JeuOuvert' ("Lonely Mountains: Downhill est ouvert.`n`n" +
            "Quittez le jeu, puis cliquez sur Réessayer.")
        if (-not $retry) { return $false }
    }
    return $true
}
