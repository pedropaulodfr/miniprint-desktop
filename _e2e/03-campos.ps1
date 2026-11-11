param()
. "$PSScriptRoot\lib.ps1"

$suite = 'Campos e preenchimento'
Backup-AppData
Start-App | Out-Null

function Get-DialogWindow([string]$Title, [int]$TimeoutSeconds = 6) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        foreach ($w in $script:Auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($w.Current.Name -eq $Title -and $w.Current.ProcessId -eq $script:Proc.Id) { return $w }
        }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $null
}

try {
    Click-Tab 'Modelos'
    Click-Text 'Novo documento'
    Start-Sleep -Milliseconds 1500

    # ---------- criar campo pelo diálogo ----------
    # Sem seleção, o cartão "Campos do documento" fica visível na dobra.
    Click-At 700 420
    Start-Sleep -Milliseconds 500
    Click-Text '+ Novo'
    Start-Sleep -Milliseconds 900

    # A janela modal aparece como descendente da janela principal para o UIA.
    $dialog = Find-ByName -NameLike 'Novo campo' -TimeoutSeconds 6
    Assert-True ($dialog -ne $null) $suite 'Diálogo de campo abriu'
    Save-Screenshot '03-dialogo-campo.png' | Out-Null

    if ($dialog) {
        $dialog = $script:Root
        $labelCond = New-Object System.Windows.Automation.PropertyCondition($script:Auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
        $edits = $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, $labelCond)
        Assert-True ($edits.Count -ge 1) $suite 'Diálogo tem campos de entrada' "edits: $($edits.Count)"
        Set-ElementValue $edits[0] 'Cliente E2E'
        Start-Sleep -Milliseconds 300

        # tipo: escolhe "Valor (decimal, R$)"
        $comboCond = New-Object System.Windows.Automation.PropertyCondition($script:Auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)
        $combo = $dialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $comboCond)
        if ($combo) {
            $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            Start-Sleep -Milliseconds 500
            $itemCond = New-Object System.Windows.Automation.PropertyCondition($script:Auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
            $items = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCond)
            foreach ($item in $items) {
                if ($item.Current.Name -like '*Valor*') {
                    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                    break
                }
            }
            Start-Sleep -Milliseconds 300
        }

        $saveCond = New-Object System.Windows.Automation.PropertyCondition($script:Auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
        $dialogButtons = $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, $saveCond)
        foreach ($b in $dialogButtons) {
            if ($b.Current.Name -like '*Salvar*') { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); break }
        }
        Start-Sleep -Milliseconds 1000
        Save-Screenshot '03-campo-criado.png' | Out-Null
        $fieldRow = Find-ByName -NameLike 'Cliente E2E' -TimeoutSeconds 4
        Assert-True ($fieldRow -ne $null) $suite 'Campo criado aparece na lista de campos'
    }

    # ---------- salvar o modelo ----------
    $nameEdit = Get-EditByValue 'Novo documento'
    if ($nameEdit) {
        Click-Element $nameEdit
        Send-Keys '^a'
        Send-Keys 'Modelo Campos E2E'
        Start-Sleep -Milliseconds 400
    }
    Click-Text 'Salvar'
    Start-Sleep -Seconds 2
    $back = Find-ByName -NameLike 'Novo documento' -TimeoutSeconds 6
    Assert-True ($back -ne $null) $suite 'Modelo com campo salvo'
    Save-Screenshot '03-lista-modelo-campo.png' | Out-Null

    # ---------- preencher ----------
    $utilizar = @(Get-Descendants -ControlType 'Button' | Where-Object { $_.Current.Name -eq 'Utilizar' })
    if ($utilizar.Count -gt 0) {
        Invoke-Element $utilizar[0]
        Start-Sleep -Milliseconds 1500
        $fillScreen = Find-ByName -NameLike 'Preencha os campos*' -TimeoutSeconds 5
        Assert-True ($fillScreen -ne $null) $suite 'Tela de preenchimento abriu'
        Save-Screenshot '03-preenchimento.png' | Out-Null

        $inputs = @(Get-Edits)
        if ($inputs.Count -gt 0) {
            Set-ElementValue $inputs[0] '150,00'
            Start-Sleep -Milliseconds 300
        }

        Click-Text 'Visualizar documento'
        Start-Sleep -Seconds 3
        $preview = Find-ByName -NameLike '*pontos ×*' -TimeoutSeconds 6
        Assert-True ($preview -ne $null) $suite 'Pré-visualização gerada' $(if ($preview) { $preview.Current.Name } else { '' })
        Save-Screenshot '03-previa-documento.png' | Out-Null
    }
    else {
        Add-Result $suite 'Preenchimento' 'SKIP' 'Botão Utilizar não encontrado'
    }
}
catch {
    Add-Result $suite 'Execução do roteiro' 'FAIL' $_.Exception.Message
    try { Save-Screenshot '03-falha.png' | Out-Null } catch { }
}
finally {
    Save-Results (Join-Path $PSScriptRoot 'resultado-03.csv')
    Stop-App
    Restore-AppData
}
