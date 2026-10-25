# Analyse statique des scripts de l'installeur : syntaxe et commandes compatibles
# Windows PowerShell 5.1 (Windows 10), BOM des fichiers, erreurs d'analyse. Échoue sur toute alerte.
param([Parameter(Mandatory = $true)][string]$ScriptsDir)

$ErrorActionPreference = 'Stop'
Import-Module PSScriptAnalyzer
$win10ps51 = 'win-48_x64_10.0.17763.0_5.1.17763.316_x64_4.0.30319.42000_framework'
$settings = @{
    IncludeRules = @('PSUseCompatibleSyntax', 'PSUseCompatibleCommands', 'PSUseCompatibleTypes',
        'PSUseBOMForUnicodeEncodedFile', 'PSAvoidUsingCmdletAliases', 'PSUseDeclaredVarsMoreThanAssignments',
        'PSAvoidAssignmentToAutomaticVariable', 'PSPossibleIncorrectComparisonWithNull')
    Rules = @{
        PSUseCompatibleSyntax = @{ Enable = $true; TargetVersions = @('5.1') }
        PSUseCompatibleCommands = @{ Enable = $true; TargetProfiles = @($win10ps51) }
        # Types chargés par Add-Type (WinForms, VisualBasic) : présents sous Windows, inconnus du profil.
        PSUseCompatibleTypes = @{ Enable = $true; TargetProfiles = @($win10ps51); IgnoreTypes = @(
            'System.Windows.Forms.Form', 'System.Windows.Forms.MessageBox', 'System.Windows.Forms.FolderBrowserDialog',
            'Microsoft.VisualBasic.FileIO.FileSystem', 'Microsoft.VisualBasic.FileIO.UIOption',
            'Microsoft.VisualBasic.FileIO.RecycleOption') }
    }
}
$problems = @()
foreach ($file in Get-ChildItem -LiteralPath $ScriptsDir -Filter '*.ps1') {
    $tokens = $null; $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    foreach ($e in $errors) { $problems += "$($file.Name):$($e.Extent.StartLineNumber) analyse : $($e.Message)" }
    foreach ($r in Invoke-ScriptAnalyzer -Path $file.FullName -Settings $settings) {
        $problems += "$($file.Name):$($r.Line) $($r.RuleName) : $($r.Message)"
    }
}
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "ÉCHEC $_" }
    exit 1
}
Write-Host "OK   $((Get-ChildItem -LiteralPath $ScriptsDir -Filter '*.ps1').Count) scripts compatibles PowerShell 5.1"
