# MelonLoader (le chargeur de mods) : détection de la version installée, installation, retrait.
# Une installation MelonLoader = version.dll + dossier MelonLoader/ à la racine du jeu.

# Version x.y.z lue dans les métadonnées d'un fichier ('' si absente).
function Get-FileVersion3([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '' }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if (-not $info.FileVersion) { return '' }
    $parts = ($info.FileVersion -split '[^0-9]+' | Where-Object { $_ -ne '' } | Select-Object -First 3)
    return ($parts -join '.')
}

function Test-MelonProxy([string]$VersionDll) {
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($VersionDll)
    return "$($info.InternalName) $($info.ProductName)" -match 'Melon'
}

# État d'un dossier : @{ Present; Version ('' = inconnue); Foreign (version.dll d'un autre outil) }.
function Get-MelonState([string]$Dir) {
    $proxy = Join-Path $Dir 'version.dll'
    $melonDir = Join-Path $Dir 'MelonLoader'
    $hasProxy = Test-Path -LiteralPath $proxy -PathType Leaf
    $hasDir = Test-Path -LiteralPath $melonDir -PathType Container
    $state = @{ Present = ($hasProxy -or $hasDir); Version = ''; Foreign = $false }
    if ($hasProxy -and -not $hasDir -and -not (Test-MelonProxy $proxy)) {
        $state.Foreign = $true
        return $state
    }
    $candidates = @($proxy, (Join-Path $melonDir 'net6\MelonLoader.dll'), (Join-Path $melonDir 'MelonLoader.dll'))
    foreach ($file in $candidates) {
        $file = $file -replace '\\', [IO.Path]::DirectorySeparatorChar
        $version = Get-FileVersion3 $file
        if ($version) { $state.Version = $version; break }
    }
    return $state
}

function Test-MelonComplete([string]$Dir) {
    return (Test-Path -LiteralPath (Join-Path $Dir 'version.dll')) -and
        (Test-Path -LiteralPath (Join-Path (Join-Path (Join-Path $Dir 'MelonLoader') 'net6') 'MelonLoader.dll'))
}

# Copie récursive qui fusionne avec l'existant (Copy-Item -Recurse imbrique parfois les dossiers).
function Copy-Tree([string]$Source, [string]$Destination) {
    $root = (Resolve-Path -LiteralPath $Source).ProviderPath.TrimEnd('\', '/')
    $count = 0
    foreach ($item in Get-ChildItem -LiteralPath $root -Recurse -Force) {
        $relative = $item.FullName.Substring($root.Length + 1)
        $target = Join-Path $Destination $relative
        if ($item.PSIsContainer) {
            if (-not (Test-Path -LiteralPath $target)) { [void](New-Item -ItemType Directory -Path $target) }
        } else {
            Copy-Item -LiteralPath $item.FullName -Destination $target -Force
            $count++
        }
    }
    Write-Journal "  $count fichier(s) copié(s) de $root vers $Destination"
}

function Compare-Version3([string]$A, [string]$B) {
    try { return ([version]$A).CompareTo([version]$B) } catch { return -1 }
}

# Texte de la question « remplacer MelonLoader $Have par $Want ? » et réponse recommandée.
function Get-ReplaceQuestion([string]$Have, [string]$Want, [int]$OtherMods) {
    $haveText = if ($Have) { "MelonLoader $Have" } else { 'une version inconnue de MelonLoader' }
    $older = (Compare-Version3 $Have $Want) -lt 0
    $text = "Ce jeu a déjà $haveText. Ovomium est prévu pour MelonLoader $Want.`n`n"
    if ($OtherMods -gt 0) {
        $text += "Vous avez $OtherMods autre(s) mod(s) : ils pourraient ne plus marcher avec $Want.`n`n"
    }
    $text += "Remplacer par MelonLoader $Want ?`n`n" +
        "Oui : remplacer (l'ancien va à la corbeille ; le prochain lancement sera long).`n" +
        "Non : garder l'actuel (Ovomium risque de ne pas marcher)."
    if ($older) { $text += "`n`nRecommandé : Oui." } else { $text += "`n`nRecommandé : Non (version plus récente)." }
    return @{ Text = $text; Default = $older }
}

# Installe ou met à jour MelonLoader. Retourne : installé, à jour, réparé, mis à jour, gardé, abandon.
function Install-MelonLoader([string]$GameDir, [string]$Payload) {
    $want = (Get-MelonState $Payload).Version
    $have = Get-MelonState $GameDir
    Write-Journal "MelonLoader fourni : $want ; présent : $($have.Present) version '$($have.Version)' étranger=$($have.Foreign)"
    if ($have.Foreign) {
        $replace = Read-YesNo 'RemplacerVersionDll' ("Le jeu contient un fichier version.dll qui n'est pas MelonLoader " +
            "(autre outil ou mod).`n`nLe remplacer par MelonLoader ? (l'ancien va à la corbeille)") $false
        if (-not $replace) { return 'abandon' }
        Remove-ToRecycleBin (Join-Path $GameDir 'version.dll')
    } elseif ($have.Present -and $have.Version -eq $want) {
        if (Test-MelonComplete $GameDir) { return 'à jour' }
        Copy-Tree $Payload $GameDir
        return 'réparé'
    } elseif ($have.Present) {
        $question = Get-ReplaceQuestion $have.Version $want (Get-OtherMods $GameDir).Count
        if (-not (Read-YesNo 'RemplacerMelonLoader' $question.Text $question.Default)) { return 'gardé' }
        Remove-MelonFiles $GameDir
        Copy-Tree $Payload $GameDir
        return 'mis à jour'
    }
    Copy-Tree $Payload $GameDir
    return 'installé'
}

# Mods et plugins autres qu'Ovomium et son plugin de mise à jour (noms de fichiers).
function Get-OtherMods([string]$GameDir) {
    $ours = @('Ovomium.dll', 'OvomiumUpdater.dll')
    $found = @()
    foreach ($sub in @('Mods', 'Plugins')) {
        $dir = Join-Path $GameDir $sub
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        $found += @(Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.dll' |
            Where-Object { $ours -notcontains $_.Name } | ForEach-Object { $_.Name })
    }
    return ,$found
}

function Remove-MelonFiles([string]$GameDir) {
    Remove-ToRecycleBin (Join-Path $GameDir 'version.dll')
    Remove-ToRecycleBin (Join-Path $GameDir 'MelonLoader')
}

# Retire MelonLoader et ses dossiers vides (Mods, Plugins, UserLibs). UserData est gardé.
function Uninstall-MelonLoader([string]$GameDir) {
    Remove-MelonFiles $GameDir
    foreach ($sub in @('Mods', 'Plugins', 'UserLibs')) {
        $dir = Join-Path $GameDir $sub
        if ((Test-Path -LiteralPath $dir) -and -not (Get-ChildItem -LiteralPath $dir -Force)) {
            Remove-Item -LiteralPath $dir -Force
            Write-Journal "  dossier vide retiré : $dir"
        }
    }
}
