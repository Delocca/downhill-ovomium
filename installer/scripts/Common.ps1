# Journal et boîtes de dialogue de l'installeur LmdMulti (Windows PowerShell 5.1 et pwsh 7).
# Sans interface (tests sous Linux, ou -Answers donné) : les questions prennent la réponse prévue
# dans $script:AnswerMap (ou leur valeur par défaut), les messages vont seulement au journal.

$script:LogPath = $null
$script:AnswerMap = @{}
$script:Interactive = $true

function Test-IsWindows {
    return $env:OS -eq 'Windows_NT'
}

function Initialize-Session([string]$LogFile, [string]$Answers) {
    $script:LogPath = $LogFile
    if ($Answers -or -not (Test-IsWindows)) { $script:Interactive = $false }
    foreach ($pair in ($Answers -split ',')) {
        $kv = $pair -split '=', 2
        if ($kv.Count -eq 2) { $script:AnswerMap[$kv[0].Trim()] = $kv[1].Trim() }
    }
    if ($script:Interactive) { Add-Type -AssemblyName System.Windows.Forms, System.Drawing }
    Write-Journal "----- $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') -----"
    Write-Journal "PowerShell $($PSVersionTable.PSVersion), Windows $([Environment]::OSVersion.Version), interactif=$($script:Interactive)"
}

function Get-DefaultLogFile([string]$Name) {
    # À côté des .bat (facile à retrouver et à envoyer) ; sinon dans %TEMP%.
    $dir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $path = Join-Path $dir $Name
    try {
        [IO.File]::AppendAllText($path, '')
        return $path
    } catch {
        return Join-Path ([IO.Path]::GetTempPath()) $Name
    }
}

function Write-Journal([string]$Message) {
    Write-Host $Message
    if ($script:LogPath) {
        try { Add-Content -LiteralPath $script:LogPath -Value $Message -Encoding UTF8 } catch { }
    }
}

function New-DialogOwner {
    # Fenêtre invisible au premier plan : sans elle, la boîte peut s'ouvrir derrière la console.
    $owner = New-Object System.Windows.Forms.Form
    $owner.TopMost = $true
    $owner.ShowInTaskbar = $false
    $owner.StartPosition = 'CenterScreen'
    $owner.Size = New-Object System.Drawing.Size(1, 1)
    $owner.Opacity = 0
    $owner.Show()
    $owner.Activate()
    return $owner
}

function Show-Box([string]$Text, [string]$Buttons, [string]$Icon, [string]$Default) {
    $owner = New-DialogOwner
    try {
        $defaultButton = if ($Default -eq 'Button2') { 'Button2' } else { 'Button1' }
        return [System.Windows.Forms.MessageBox]::Show($owner, $Text, 'LmdMulti',
            $Buttons, $Icon, $defaultButton).ToString()
    } finally {
        $owner.Close()
        $owner.Dispose()
    }
}

# Message d'information, d'avertissement ou d'erreur (Kind : Info, Warning, Error).
function Show-Message([string]$Text, [string]$Kind = 'Info') {
    Write-Journal "[$Kind] $Text"
    if (-not $script:Interactive) { return }
    $icon = @{ Info = 'Information'; Warning = 'Warning'; Error = 'Error' }[$Kind]
    [void](Show-Box $Text 'OK' $icon 'Button1')
}

# Question Oui/Non. $Key nomme la question dans le journal (et dans -Answers pour les tests).
function Read-YesNo([string]$Key, [string]$Text, [bool]$Default) {
    $answer = $Default
    if (-not $script:Interactive) {
        if ($script:AnswerMap.ContainsKey($Key)) { $answer = $script:AnswerMap[$Key] -eq 'Oui' }
    } else {
        $default = if ($Default) { 'Button1' } else { 'Button2' }
        $answer = (Show-Box $Text 'YesNo' 'Question' $default) -eq 'Yes'
    }
    $label = if ($answer) { 'Oui' } else { 'Non' }
    Write-Journal "[Question $Key] $($Text -replace "`r?`n", ' ') -> $label"
    return $answer
}

# Réessayer/Annuler (ex. jeu encore ouvert). Sans interface : Annuler.
function Read-Retry([string]$Key, [string]$Text) {
    Write-Journal "[Question $Key] $($Text -replace "`r?`n", ' ')"
    if (-not $script:Interactive) { return $false }
    return (Show-Box $Text 'RetryCancel' 'Warning' 'Button1') -eq 'Retry'
}

# Sélecteur de dossier. Retourne '' si annulé (ou sans interface).
function Read-Folder([string]$Description) {
    if (-not $script:Interactive) {
        if ($script:AnswerMap.ContainsKey('Dossier')) { return $script:AnswerMap['Dossier'] }
        return ''
    }
    $owner = New-DialogOwner
    try {
        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $dialog.Description = $Description
        $dialog.ShowNewFolderButton = $false
        if ($dialog.ShowDialog($owner) -eq 'OK') { return $dialog.SelectedPath }
        return ''
    } finally {
        $owner.Close()
        $owner.Dispose()
    }
}

# Envoie à la corbeille (récupérable) ; à défaut (Linux, disque sans corbeille), supprime.
function Remove-ToRecycleBin([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $isDir = Test-Path -LiteralPath $Path -PathType Container
    try {
        if (-not (Test-IsWindows)) { throw 'pas de corbeille' }
        Add-Type -AssemblyName Microsoft.VisualBasic
        $fs = [Microsoft.VisualBasic.FileIO.FileSystem]
        $ui = [Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs
        $bin = [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin
        if ($isDir) { $fs::DeleteDirectory($Path, $ui, $bin) } else { $fs::DeleteFile($Path, $ui, $bin) }
        Write-Journal "  corbeille : $Path"
    } catch {
        Remove-Item -LiteralPath $Path -Recurse -Force
        Write-Journal "  supprimé : $Path"
    }
}

# Retire la marque « téléchargé d'Internet » des fichiers copiés (sans effet hors Windows).
function Clear-DownloadMark([string]$Path) {
    if (-not (Test-IsWindows)) { return }
    Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue |
        Unblock-File -ErrorAction SilentlyContinue
}
