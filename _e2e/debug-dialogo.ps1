. "$PSScriptRoot\lib.ps1"
Start-App | Out-Null
try {
    Click-Tab 'Modelos'
    Click-Text 'Novo documento'
    Start-Sleep -Milliseconds 1500
    Click-At 700 420
    Start-Sleep -Milliseconds 400

    $btn = $null
    foreach ($b in Get-Descendants -ControlType 'Button') { if ($b.Current.Name -eq '+ Novo') { $btn = $b; break } }
    if (-not $btn) { Write-Output 'botao + Novo nao achado; tentando pelo texto'; $btn = Find-ByName -NameLike 'Novo campo...' }
    if ($btn) {
        Write-Output ("elemento: [" + $btn.Current.Name + "] tipo=" + $btn.Current.ControlType.ProgrammaticName + " em " + [int]$btn.Current.BoundingRectangle.X + "," + [int]$btn.Current.BoundingRectangle.Y)
        Invoke-Element $btn
        Start-Sleep -Seconds 2
    }
    else {
        Write-Output 'botao Novo nao encontrado'
    }

    Write-Output '--- janelas de nivel superior ---'
    foreach ($w in $script:Auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($w.Current.ProcessId -eq $script:Proc.Id) { Write-Output ("  [" + $w.Current.Name + "] classe=" + $w.Current.ClassName) }
    }
    Save-Screenshot 'debug-dialogo.png' | Out-Null
}
finally {
    Stop-App
}
