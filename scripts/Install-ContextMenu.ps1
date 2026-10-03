# Install-ContextMenu.ps1
# Registers the File Organizer Explorer integration for the CURRENT user (no admin needed):
#   Right-click a file  -> "Move to Organizer" -> one entry per Quick Destination
#   Right-click a folder -> "Add as Organizer Destination"
#   Send To             -> one shortcut per destination (fallback)
# Reads destinations from the app's own config:
#   %AppData%\FileOrganizer\config.json
# Run Install again after adding/renaming destinations in the app.
# The app can do the same thing itself: Settings -> Install / Refresh Context Menu.
# This script exists so the installer (or a power user) can register without opening the app.

[CmdletBinding()]
param(
    # Path to FileOrganizer.exe (defaults to this script's folder, then the app install folder).
    [string]$ExePath = "",
    # Path to config.json (defaults to %AppData%\FileOrganizer\config.json).
    [string]$ConfigPath = "$env:APPDATA\FileOrganizer\config.json"
)

$ErrorActionPreference = "Stop"

if (-not $ExePath) {
    $candidates = @(
        (Join-Path $PSScriptRoot "FileOrganizer.exe"),
        "$env:LOCALAPPDATA\Programs\FileOrganizer\FileOrganizer.exe",
        "$env:ProgramFiles\FileOrganizer\FileOrganizer.exe"
    )
    $ExePath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $ExePath -or -not (Test-Path $ExePath)) {
    throw "FileOrganizer.exe not found. Pass -ExePath explicitly."
}
$ExePath = (Resolve-Path $ExePath).Path

if (-not (Test-Path $ConfigPath)) {
    throw "Config not found at $ConfigPath. Run File Organizer once first so it creates its config."
}
$config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
$destinations = @($config.Destinations)
if ($destinations.Count -eq 0) { throw "The config has no destinations yet." }

$roots = @(
    "HKCU:\Software\Classes\*\shell\MoveToOrganizer",
    "HKCU:\Software\Classes\Directory\shell\MoveToOrganizer"
)
foreach ($menuRoot in $roots) {
    # Reset any previous registration so renamed/removed destinations disappear.
    Remove-Item $menuRoot -Recurse -Force -ErrorAction SilentlyContinue

    New-Item -Path $menuRoot -Force | Out-Null
    Set-ItemProperty -Path $menuRoot -Name "MUIVerb" -Value "Move to Organizer"
    Set-ItemProperty -Path $menuRoot -Name "SubCommands" -Value ""
    Set-ItemProperty -Path $menuRoot -Name "Icon" -Value "`"$ExePath`",0"

    New-Item -Path "$menuRoot\shell" -Force | Out-Null
    foreach ($dest in ($destinations | Sort-Object SortOrder)) {
        $verb = "$menuRoot\shell\MoveTo_$($dest.Id)"
        New-Item -Path "$verb\command" -Force | Out-Null
        Set-ItemProperty -Path $verb -Name "MUIVerb" -Value "$($dest.Icon) $($dest.Name)"
        Set-Item -Path "$verb\command" -Value "`"$ExePath`" --move-to `"$($dest.Id)`" `"%1`""
        Write-Host "  + $($dest.Name)"
    }
    $addVerb = "$menuRoot\shell\ZZ_AddDestination"
    New-Item -Path "$addVerb\command" -Force | Out-Null
    Set-ItemProperty -Path $addVerb -Name "MUIVerb" -Value "+ Add Destination..."
    Set-Item -Path "$addVerb\command" -Value "`"$ExePath`" --add-destination `"%1`""
}

# Right-click a folder -> add it as a destination.
$folderKey = "HKCU:\Software\Classes\Directory\shell\OrganizerAddDestination"
Remove-Item $folderKey -Recurse -Force -ErrorAction SilentlyContinue
New-Item -Path "$folderKey\command" -Force | Out-Null
Set-ItemProperty -Path $folderKey -Name "MUIVerb" -Value "Add as Organizer Destination"
Set-Item -Path "$folderKey\command" -Value "`"$ExePath`" --add-destination `"%1`""

# Send To fallback shortcuts (one per destination).
$sendTo = [Environment]::GetFolderPath("SendTo")
Get-ChildItem $sendTo -Filter "Move to *.lnk" -ErrorAction SilentlyContinue | Remove-Item -Force
$shell = New-Object -ComObject WScript.Shell
foreach ($dest in ($destinations | Sort-Object SortOrder)) {
    $link = $shell.CreateShortcut((Join-Path $sendTo "Move to $($dest.Name).lnk"))
    $link.TargetPath = $ExePath
    $link.Arguments = "--move-to `"$($dest.Id)`""
    $link.Description = "Move file to $($dest.Name) with File Organizer"
    $link.Save()
}

Write-Host "File Organizer context menu installed for $env:USERNAME."
Write-Host "Right-click any file -> Show more options -> Move to Organizer (Windows 11), or Send To -> Move to ..."
