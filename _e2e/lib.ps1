# Biblioteca de automação E2E do MiniPrint Desktop (WPF + UI Automation)
# Uso: . "$PSScriptRoot\lib.ps1"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class MpE2E {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$script:Auto = [System.Windows.Automation.AutomationElement]
$script:Results = New-Object System.Collections.Generic.List[object]
$script:EvidenceDir = Join-Path $PSScriptRoot 'evidencias'
$script:Proc = $null
$script:Root = $null
$script:Hwnd = [IntPtr]::Zero

if (-not (Test-Path $script:EvidenceDir)) { New-Item -ItemType Directory -Path $script:EvidenceDir -Force | Out-Null }

# ------------------------------------------------------------------ aplicativo

function Start-App {
    param([switch]$Fresh)
    $exe = 'D:\Documentos\projects\MiniPrint-Desktop\src\MiniPrint.Desktop\bin\Debug\net8.0-windows10.0.19041.0\MiniPrint.exe'
    $script:Proc = Start-Process -FilePath $exe -PassThru
    $hwnd = [IntPtr]::Zero
    for ($i = 0; $i -lt 40 -and $hwnd -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 400
        $script:Proc.Refresh()
        if ($script:Proc.HasExited) { throw "O aplicativo encerrou ao iniciar (código $($script:Proc.ExitCode))." }
        $hwnd = $script:Proc.MainWindowHandle
    }
    if ($hwnd -eq [IntPtr]::Zero) { throw 'Janela principal não apareceu.' }
    $script:Hwnd = $hwnd
    [void][MpE2E]::MoveWindow($hwnd, 20, 20, 1260, 820, $true)
    [void][MpE2E]::SetForegroundWindow($hwnd)
    Start-Sleep -Milliseconds 900
    $script:Root = $script:Auto::FromHandle($hwnd)
    return $script:Proc
}

function Stop-App {
    if ($script:Proc -and -not $script:Proc.HasExited) {
        Stop-Process -Id $script:Proc.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 400
    }
    $script:Proc = $null
    $script:Root = $null
}

function Get-WindowRect {
    $rect = New-Object MpE2E+RECT
    [void][MpE2E]::GetWindowRect($script:Hwnd, [ref]$rect)
    return $rect
}

# ------------------------------------------------------------------ localizar

function Get-Descendants {
    param([string]$ControlType, [string]$NameLike)
    $cond = if ($ControlType) {
        New-Object System.Windows.Automation.PropertyCondition($script:Auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::$ControlType)
    } else { [System.Windows.Automation.Condition]::TrueCondition }
    $all = $script:Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $list = New-Object System.Collections.Generic.List[object]
    foreach ($item in $all) {
        if (-not $NameLike -or $item.Current.Name -like $NameLike) { $list.Add($item) }
    }
    return $list
}

function Find-ByName {
    param([string]$NameLike, [string]$ControlType, [int]$TimeoutSeconds = 6)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $hop = Get-Descendants -ControlType $ControlType -NameLike $NameLike
        if ($hop.Count -gt 0) { return $hop[0] }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Wait-ForName {
    param([string]$NameLike, [int]$TimeoutSeconds = 8)
    return (Find-ByName -NameLike $NameLike -TimeoutSeconds $TimeoutSeconds) -ne $null
}

# ------------------------------------------------------------------ interação

function Click-At {
    param([int]$X, [int]$Y)
    [void][MpE2E]::SetCursorPos($X, $Y)
    Start-Sleep -Milliseconds 120
    [MpE2E]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [MpE2E]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 350
}

function Click-Tab {
    param([string]$Name)
    $label = Find-ByName -NameLike $Name -ControlType 'TabItem' -TimeoutSeconds 3
    if ($null -eq $label) { $label = Find-ByName -NameLike $Name -ControlType 'Text' -TimeoutSeconds 6 }
    if ($null -eq $label) { throw "Aba '$Name' não encontrada." }
    # Ativa a janela por um clique na barra de título, depois clica na aba duas vezes.
    $wr = Get-WindowRect
    Click-At ([int](($wr.Left + $wr.Right) / 2)) ([int]($wr.Top + 12))
    Start-Sleep -Milliseconds 350

    $r = $label.Current.BoundingRectangle
    $x = [int]($r.X + $r.Width / 2)
    $y = [int]($r.Y + $r.Height / 2)
    Click-At $x $y
    Start-Sleep -Milliseconds 300
    Click-At $x $y
    Start-Sleep -Milliseconds 1200
}
function Click-Element {
    param($Element, [switch]$DoubleClick)
    if ($null -eq $Element) { throw 'Elemento nulo para clique.' }
    $rect = $Element.Current.BoundingRectangle
    $x = [int]($rect.X + $rect.Width / 2)
    $y = [int]($rect.Y + $rect.Height / 2)
    Click-At $x $y
    if ($DoubleClick) { Click-At $x $y }
}

function Invoke-Element {
    param($Element)
    if ($null -eq $Element) { throw 'Elemento nulo para invocar.' }
    try {
        $pattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
        Start-Sleep -Milliseconds 500
    } catch {
        Click-Element $Element
    }
}

function Select-Element {
    param($Element)
    try {
        $Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 600
    } catch {
        Click-Element $Element
    }
}

function Set-ElementValue {
    param($Element, [string]$Value)
    if ($null -eq $Element) { throw 'Elemento nulo para definir valor.' }
    try {
        $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
        Start-Sleep -Milliseconds 250
    } catch {
        Click-Element $Element
        [System.Windows.Forms.SendKeys]::SendWait('^a')
        [System.Windows.Forms.SendKeys]::SendWait($Value)
        Start-Sleep -Milliseconds 250
    }
}

function Get-ElementValue {
    param($Element)
    try {
        return $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    } catch {
        return $Element.Current.Name
    }
}

function Drag-FromTo {
    param([int]$X1, [int]$Y1, [int]$X2, [int]$Y2)
    [void][MpE2E]::SetCursorPos($X1, $Y1)
    Start-Sleep -Milliseconds 150
    [MpE2E]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 150

    $steps = 12
    for ($i = 1; $i -le $steps; $i++) {
        $x = [int]($X1 + ($X2 - $X1) * $i / $steps)
        $y = [int]($Y1 + ($Y2 - $Y1) * $i / $steps)
        [void][MpE2E]::SetCursorPos($x, $y)
        Start-Sleep -Milliseconds 35
    }

    Start-Sleep -Milliseconds 120
    [MpE2E]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 300
}

function Send-Keys {
    param([string]$Keys, [int]$PauseMs = 350)
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
    Start-Sleep -Milliseconds $PauseMs
}

function Click-Text {
    param([string]$Name, [switch]$Twice)
    $el = Find-ByName -NameLike $Name -TimeoutSeconds 6
    if ($null -eq $el) { throw "Texto '$Name' não encontrado." }

    # Botões fora da área visível (painéis com rolagem) só respondem ao padrão Invoke.
    try {
        if ($el.GetSupportedPatterns() -contains [System.Windows.Automation.InvokePattern]::Pattern) {
            $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 600
            return
        }
    } catch { }
    $r = $el.Current.BoundingRectangle
    if ([double]::IsInfinity($r.Width) -or $r.Width -le 0) { throw "Texto '$Name' sem área clicável." }
    $x = [int]($r.X + $r.Width / 2)
    $y = [int]($r.Y + $r.Height / 2)
    Click-At $x $y
    if ($Twice) { Start-Sleep -Milliseconds 200; Click-At $x $y }
}

function Get-Edits {
    $edits = Get-Descendants -ControlType 'Edit'
    return $edits | Sort-Object { [int]$_.Current.BoundingRectangle.Y }, { [int]$_.Current.BoundingRectangle.X }
}

function Get-EditByValue {
    param([string]$Value)
    foreach ($e in Get-Edits) {
        try {
            $v = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
            if ($v -eq $Value) { return $e }
        } catch { }
    }
    return $null
}

function Get-EditValueAt {
    param([int]$Index)
    $edits = @(Get-Edits)
    if ($Index -ge $edits.Count) { return $null }
    return (Get-ElementValue $edits[$Index])
}

function Get-PositionValues {
    # Lê X, Y, Largura e Altura do painel de propriedades pela linha do topo.
    $edits = @(Get-Edits | Where-Object {
        $r = $_.Current.BoundingRectangle
        $r.Y -ge 240 -and $r.Y -le 320 -and $r.X -ge 900
    } | Sort-Object { [int]$_.Current.BoundingRectangle.X })
    $values = @()
    foreach ($e in $edits) { $values += (Get-ElementValue $e) }
    return $values
}

function Get-SelectionText {
    $el = Find-ByName -NameLike '*selecionado*' -TimeoutSeconds 2
    if ($el) { return $el.Current.Name }
    return ''
}
function Get-TextCount {
    param([string]$NameLike)
    return @(Get-Descendants -ControlType 'Text' -NameLike $NameLike).Count
}
# ------------------------------------------------------------------ evidências

function Save-Screenshot {
    param([string]$Name)
    $rect = Get-WindowRect
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    $path = Join-Path $script:EvidenceDir $Name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $path
}

# ------------------------------------------------------------------ asserts

function Add-Result {
    param([string]$Suite, [string]$Test, [string]$Status, [string]$Detail = '')
    $item = [pscustomobject]@{
        Suite = $Suite
        Test = $Test
        Status = $Status
        Detail = $Detail
        Time = (Get-Date).ToString('HH:mm:ss')
    }
    $script:Results.Add($item)
    $color = switch ($Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } default { 'Yellow' } }
    Write-Host ("[{0}] {1} :: {2}{3}" -f $Status, $Suite, $Test, $(if ($Detail) { " — $Detail" } else { '' })) -ForegroundColor $color
}

function Assert-True {
    param([bool]$Condition, [string]$Suite, [string]$Test, [string]$Detail = '')
    if ($Condition) { Add-Result $Suite $Test 'PASS' $Detail }
    else { Add-Result $Suite $Test 'FAIL' $Detail }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Suite, [string]$Test, [string]$Detail = '')
    $ok = ($Expected -eq $Actual)
    $text = if ($ok) { "$Detail ($Actual)" } else { "esperado '$Expected', obtido '$Actual'. $Detail" }
    if ($ok) { Add-Result $Suite $Test 'PASS' $text } else { Add-Result $Suite $Test 'FAIL' $text }
}

function Save-Results {
    param([string]$Path)
    $script:Results | Export-Csv -Path $Path -NoTypeInformation -Encoding UTF8
    $pass = ($script:Results | Where-Object Status -eq 'PASS').Count
    $fail = ($script:Results | Where-Object Status -eq 'FAIL').Count
    Write-Host ""
    Write-Host ("TOTAL: {0} | PASSOU: {1} | FALHOU: {2}" -f $script:Results.Count, $pass, $fail) -ForegroundColor Cyan
}

# ------------------------------------------------------------------ dados isolados

function Backup-AppData {
    $src = Join-Path $env:LOCALAPPDATA 'MiniPrintDesktop'
    $dst = Join-Path $PSScriptRoot 'data-backup'
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    if (Test-Path $src) { Copy-Item $src $dst -Recurse -Force }
    Write-Host "Backup dos dados reais em $dst" -ForegroundColor DarkGray
}

function Restore-AppData {
    $src = Join-Path $env:LOCALAPPDATA 'MiniPrintDesktop'
    $dst = Join-Path $PSScriptRoot 'data-backup'
    if (-not (Test-Path $dst)) { return }
    if (Test-Path $src) { Remove-Item $src -Recurse -Force }
    Copy-Item $dst $src -Recurse -Force
    Write-Host "Dados reais restaurados de $dst" -ForegroundColor DarkGray
}
