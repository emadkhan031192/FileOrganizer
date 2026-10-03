# Uninstall-ContextMenu.ps1
# Removes everything Install-ContextMenu.ps1 (or the app's Settings screen) registered
# for the current user. Does not touch the app, its config, or any organized files.
$ErrorActionPreference = "SilentlyContinue"

Remove-Item "HKCU:\Software\Classes\*\shell\MoveToOrganizer" -Recurse -Force
Remove-Item "HKCU:\Software\Classes\Directory\shell\OrganizerAddDestination" -Recurse -Force

$sendTo = [Environment]::GetFolderPath("SendTo")
Get-ChildItem $sendTo -Filter "Move to *.lnk" | Remove-Item -Force

Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "FileOrganizer"

Write-Host "File Organizer Explorer integration removed."
