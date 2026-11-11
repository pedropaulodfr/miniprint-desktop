param()
. "$PSScriptRoot\lib.ps1"

$suite = 'Campos independentes'
Backup-AppData
Start-App | Out-Null

try {
    $ok = $false
    for ($i = 0; $i -lt 3 -and -not $ok; $i++) {
        Click-Tab 'Modelos'
        try {
            Click-Text 'Novo documento'
            $ok = $true
        }
        catch {
            Start-Sleep -Milliseconds 800
        }
    }
    if (-not $ok) { throw 'Nao foi possivel abrir um novo documento.' }
    Start-Sleep -Milliseconds 1500

    # Dois campos CPF/CNPJ inseridos pela paleta
    Click-Text 'CPF / CNPJ'
    Start-Sleep -Milliseconds 700
    Click-Text 'CPF / CNPJ'
    Start-Sleep -Milliseconds 700
    Save-Screenshot '04-dois-campos-cpf.png' | Out-Null

    $segundo = Get-EditByValue 'CPF/CNPJ 2'
    Assert-True ($segundo -ne $null) $suite 'Segundo campo nasce com rótulo próprio' 'CPF/CNPJ 2'

    if ($segundo) {
        Click-Element $segundo
        Send-Keys '^a'
        Send-Keys 'CPF do receptor'
        Start-Sleep -Milliseconds 600
    }

    $renomeado = Get-EditByValue 'CPF do receptor'
    Assert-True ($renomeado -ne $null) $suite 'Rótulo do segundo campo foi alterado'

    # O primeiro campo continua com o rótulo original no canvas e na lista
    $originalNoCanvas = Get-TextCount '«CPF/CNPJ»'
    $novoNoCanvas = Get-TextCount '«CPF do receptor»'
    Assert-True ($originalNoCanvas -ge 1) $suite 'Primeiro campo mantém «CPF/CNPJ»' "ocorrências: $originalNoCanvas"
    Assert-True ($novoNoCanvas -ge 1) $suite 'Segundo campo aparece como «CPF do receptor»' "ocorrências: $novoNoCanvas"
    Assert-True ((Get-TextCount 'CPF/CNPJ 2') -eq 0) $suite 'Nenhum elemento ficou com o rótulo antigo'

    Save-Screenshot '04-cpf-renomeado.png' | Out-Null
}
catch {
    Add-Result $suite 'Execução do roteiro' 'FAIL' $_.Exception.Message
    try { Save-Screenshot '04-falha.png' | Out-Null } catch { }
}
finally {
    Save-Results (Join-Path $PSScriptRoot 'resultado-04.csv')
    Stop-App
    Restore-AppData
}
