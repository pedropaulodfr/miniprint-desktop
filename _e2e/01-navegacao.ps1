param()
. "$PSScriptRoot\lib.ps1"

$suite = 'Navegação'
Backup-AppData
Start-App | Out-Null

try {
    # 1. Abrir a aba Modelos (clique no cabeçalho da aba)
    $tabs = Get-Descendants -ControlType 'TabItem'
    Assert-True ($tabs.Count -ge 6) $suite 'Aba lista as 6 seções' "abas: $($tabs.Count)"

    Click-Tab 'Modelos'

    $newButton = Find-ByName -NameLike 'Novo documento' -ControlType 'Button'
    Assert-True ($newButton -ne $null) $suite 'Aba Modelos carregou (botão Novo documento)'
    Save-Screenshot '01-lista-modelos.png' | Out-Null

    # 2. Criar novo documento
    Invoke-Element $newButton
    Start-Sleep -Milliseconds 1500

    $palette = Find-ByName -NameLike 'Texto simples' -TimeoutSeconds 6
    Assert-True ($palette -ne $null) $suite 'Editor visual abriu (paleta visível)'

    $props = Find-ByName -NameLike 'Campos do documento' -TimeoutSeconds 4
    Assert-True ($props -ne $null) $suite 'Painel de campos visível'

    Save-Screenshot '01-editor-novo.png' | Out-Null

    # 3. Mapa de controles do editor (para os próximos roteiros)
    $dump = New-Object System.Collections.Generic.List[string]
    foreach ($type in @('Button', 'Edit', 'ComboBox', 'CheckBox', 'Slider', 'TabItem', 'Text')) {
        $items = Get-Descendants -ControlType $type
        foreach ($item in $items) {
            $r = $item.Current.BoundingRectangle
            if ([double]::IsInfinity($r.Width) -or [double]::IsInfinity($r.Height) -or $r.Width -le 0) { continue }
            $value = ''
            try {
                if ($item.GetSupportedPatterns() -contains [System.Windows.Automation.ValuePattern]::Pattern) {
                    $value = ' = "' + $item.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value + '"'
                }
            } catch { }
            $dump.Add(("{0} | {1}{2} | {3},{4} {5}x{6}" -f $type, $item.Current.Name, $value, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height))
        }
    }
    $dump | Set-Content (Join-Path $PSScriptRoot 'editor-controls.txt') -Encoding UTF8

    # 4. Voltar à lista
    $back = Find-ByName -NameLike 'Voltar' -ControlType 'Button'
    if ($back) {
        Invoke-Element $back
        Start-Sleep -Milliseconds 1200
        $confirm = Find-ByName -NameLike 'Sim' -ControlType 'Button' -TimeoutSeconds 2
        if ($confirm) { Invoke-Element $confirm; Start-Sleep -Milliseconds 800 }
        $listAgain = Find-ByName -NameLike 'Novo documento' -ControlType 'Button' -TimeoutSeconds 5
        Assert-True ($listAgain -ne $null) $suite 'Voltar retorna à lista de modelos'
        Save-Screenshot '01-volta-lista.png' | Out-Null
    }
}
catch {
    Add-Result $suite 'Execução do roteiro' 'FAIL' $_.Exception.Message
}
finally {
    Save-Results (Join-Path $PSScriptRoot 'resultado-01.csv')
    Stop-App
    Restore-AppData
}
