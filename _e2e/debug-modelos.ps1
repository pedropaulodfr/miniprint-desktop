. "$PSScriptRoot\lib.ps1"
Start-App | Out-Null
try {
    Start-Sleep -Seconds 2
    Write-Output '--- botoes antes ---'
    foreach ($b in Get-Descendants -ControlType 'Button') { Write-Output ('  [' + $b.Current.Name + ']') }
    Click-Tab 'Modelos'
    Start-Sleep -Seconds 1
    Write-Output '--- botoes depois ---'
    foreach ($b in Get-Descendants -ControlType 'Button') { Write-Output ('  [' + $b.Current.Name + ']') }
    Save-Screenshot 'debug-modelos.png' | Out-Null
}
finally { Stop-App }
