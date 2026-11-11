. "$PSScriptRoot\lib.ps1"

Start-App | Out-Null
try {
    $modelos = Find-ByName -NameLike 'Modelos' -ControlType 'Text'
    $r = $modelos.Current.BoundingRectangle
    Write-Output ("label em {0},{1} {2}x{3}" -f [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)

    Click-At ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 400
    Click-At ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 1500

    $tabs = Get-Descendants -ControlType 'TabItem'
    $i = 0
    foreach ($t in $tabs) { Write-Output ("tab {0}: selected={1} rect={2},{3}" -f $i, $t.Current.IsSelected, [int]$t.Current.BoundingRectangle.X, [int]$t.Current.BoundingRectangle.Y); $i++ }

    $buttons = Get-Descendants -ControlType 'Button'
    Write-Output "botoes:"
    foreach ($b in $buttons) { Write-Output ("  [" + $b.Current.Name + "]") }

    Save-Screenshot 'debug-tab.png' | Out-Null
}
finally {
    Stop-App
}
