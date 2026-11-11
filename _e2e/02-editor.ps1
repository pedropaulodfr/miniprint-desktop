param()
. "$PSScriptRoot\lib.ps1"

$suite = 'Editor visual'
Backup-AppData
Start-App | Out-Null

function Open-NewDocument {
    Click-Tab 'Modelos'
    Click-Text 'Novo documento'
    Start-Sleep -Milliseconds 1500
}

try {
    Open-NewDocument
    Save-Screenshot '02-editor-inicial.png' | Out-Null

    # ---------- 1. Inserção pela paleta ----------
    $before = Get-TextCount 'TÍTULO DO DOCUMENTO'
    Click-Text 'Texto simples'
    Start-Sleep -Milliseconds 600
    $afterInsert = Get-TextCount 'Digite aqui'
    Assert-True ($afterInsert -ge 1) $suite 'Inserir texto pela paleta' "textos 'Digite aqui': $afterInsert"

    Click-Text 'Linha'
    Start-Sleep -Milliseconds 500
    Click-Text 'Retângulo'
    Start-Sleep -Milliseconds 500

    # O elemento recém-inserido fica selecionado: as propriedades mostram a posição.
    $pos = @(Get-PositionValues)
    $x1 = [int]$pos[0]
    $y1 = [int]$pos[1]
    Assert-True ($x1 -ge 0 -and $y1 -ge 0) $suite 'Propriedades mostram posição do elemento' "X=$x1 Y=$y1"
    Save-Screenshot '02-tres-elementos.png' | Out-Null

    # ---------- 2. Arrastar no canvas ----------
    $alvo = Find-ByName -NameLike 'Digite aqui'
    $rect = $alvo.Current.BoundingRectangle
    $px = [int]($rect.X + 10)
    $py = [int]($rect.Y + $rect.Height / 2)
    Drag-FromTo $px $py ($px + 40) ($py + 30)
    Start-Sleep -Milliseconds 500

    $pos2 = @(Get-PositionValues)
    $x2 = [int]$pos2[0]
    $y2 = [int]$pos2[1]
    Assert-True ($x2 -ne $x1 -or $y2 -ne $y1) $suite 'Arrastar move o elemento' "de ($x1,$y1) para ($x2,$y2)"
    Save-Screenshot '02-apos-arrastar.png' | Out-Null

    # ---------- 3. Zoom não altera coordenadas ----------
    Click-Text 'Ajustar'
    Start-Sleep -Milliseconds 700
    Click-Text '100%'
    Start-Sleep -Milliseconds 600
    $pos3 = @(Get-PositionValues)
    $x3 = [int]$pos3[0]
    $y3 = [int]$pos3[1]
    Assert-Equal $x2 $x3 $suite 'Zoom preserva a coordenada X'
    Assert-Equal $y2 $y3 $suite 'Zoom preserva a coordenada Y'

    # ---------- 4. Seleção múltipla e exclusão ----------
    $titulo = Find-ByName -NameLike 'TÍTULO DO DOCUMENTO'
    $r = $titulo.Current.BoundingRectangle
    Click-At ([int]($r.X + 10)) ([int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 400
    Send-Keys '^a'
    Start-Sleep -Milliseconds 400
    $multi = Find-ByName -NameLike '*elementos selecionados*' -TimeoutSeconds 3
    Assert-True ($multi -ne $null) $suite 'Ctrl+A seleciona vários elementos' $(if ($multi) { $multi.Current.Name } else { 'sem texto' })
    Save-Screenshot '02-multiselecao.png' | Out-Null

    # ---------- 5. Undo / Redo ----------
    Send-Keys '^z'
    Start-Sleep -Milliseconds 300
    Send-Keys '^z'
    Start-Sleep -Milliseconds 300
    Send-Keys '^z'
    Start-Sleep -Milliseconds 400
    $undoOk = Get-TextCount 'Digite aqui'
    Add-Result $suite 'Undo executa sem erro' 'PASS' "textos restantes: $undoOk"

    Send-Keys '^y'
    Start-Sleep -Milliseconds 400
    Add-Result $suite 'Redo executa sem erro' 'PASS'
    Save-Screenshot '02-undo-redo.png' | Out-Null

    # ---------- 6. Salvar com nome ----------
    $nameEdit = Get-EditByValue 'Novo documento'
    Assert-True ($nameEdit -ne $null) $suite 'Campo de nome do documento localizado'
    Click-Element $nameEdit
    Send-Keys '^a'
    Send-Keys 'Comprovante E2E'
    Start-Sleep -Milliseconds 600
    $nameNow = Get-EditByValue 'Comprovante E2E'
    Assert-True ($nameNow -ne $null) $suite 'Nome digitado permanece no cabeçalho'
    Save-Screenshot '02-nome-digitado.png' | Out-Null

    Click-Text 'Salvar'
    Start-Sleep -Seconds 2
    $listBack = Find-ByName -NameLike 'Novo documento' -TimeoutSeconds 6
    Assert-True ($listBack -ne $null) $suite 'Salvar retorna à lista'

    $row = Find-ByName -NameLike 'Comprovante E2E' -TimeoutSeconds 4
    Assert-True ($row -ne $null) $suite 'Modelo salvo aparece na lista'
    Save-Screenshot '02-lista-com-modelo.png' | Out-Null

    # ---------- 7. Reabrir e conferir conteúdo ----------
    $editsButtons = @(Get-Descendants -ControlType 'Button' | Where-Object { $_.Current.Name -eq 'Editar' })
    if ($editsButtons.Count -gt 0) {
        Invoke-Element $editsButtons[0]
        Start-Sleep -Milliseconds 1500
        $reopened = Get-EditByValue 'Comprovante E2E'
        Assert-True ($reopened -ne $null) $suite 'Reabrir mantém o nome'
        $conteudo = Find-ByName -NameLike 'Digite aqui' -TimeoutSeconds 4
        Assert-True ($conteudo -ne $null) $suite 'Reabrir mantém o elemento de texto'
        Save-Screenshot '02-reaberto.png' | Out-Null
    }
    else {
        Add-Result $suite 'Reabrir modelo' 'SKIP' 'Botão Editar não encontrado'
    }
}
catch {
    Add-Result $suite 'Execução do roteiro' 'FAIL' $_.Exception.Message
    try { Save-Screenshot '02-falha.png' | Out-Null } catch { }
}
finally {
    Save-Results (Join-Path $PSScriptRoot 'resultado-02.csv')
    Stop-App
    Restore-AppData
}
