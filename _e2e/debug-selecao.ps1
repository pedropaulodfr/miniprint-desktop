. "$PSScriptRoot\lib.ps1"
Start-App | Out-Null
try {
    Click-Tab 'Modelos'
    Click-Text 'Novo documento'
    Start-Sleep -Milliseconds 1500
    Click-Text 'Texto simples'
    Start-Sleep -Milliseconds 600
    Click-Text 'Linha'
    Start-Sleep -Milliseconds 600

    Write-Output '--- textos com selecion ---'
    foreach ($t in Get-Descendants -ControlType 'Text') {
        if ($t.Current.Name -match 'selecion') { Write-Output ("  [" + $t.Current.Name + "] em " + [int]$t.Current.BoundingRectangle.X + "," + [int]$t.Current.BoundingRectangle.Y) }
    }

    $titulo = Find-ByName -NameLike 'Digite aqui'
    $r = $titulo.Current.BoundingRectangle
    Click-At ([int]($r.X + 8)) ([int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 500
    Write-Output '--- apos clicar no texto ---'
    foreach ($t in Get-Descendants -ControlType 'Text') {
        if ($t.Current.Name -match 'selecion') { Write-Output ("  [" + $t.Current.Name + "]") }
    }

    Send-Keys '^a'
    Start-Sleep -Milliseconds 600
    Write-Output '--- apos Ctrl+A ---'
    foreach ($t in Get-Descendants -ControlType 'Text') {
        if ($t.Current.Name -match 'selecion') { Write-Output ("  [" + $t.Current.Name + "]") }
    }
    Save-Screenshot 'debug-selecao.png' | Out-Null
}
finally {
    Stop-App
}
