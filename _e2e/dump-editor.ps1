Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = 'D:\Documentos\projects\MiniPrint-Desktop\src\MiniPrint.Desktop\bin\Debug\net8.0-windows10.0.19041.0\MiniPrint.exe'
$proc = Start-Process -FilePath $exe -PassThru
$hwnd = 0
for ($i = 0; $i -lt 30 -and $hwnd -eq 0; $i++) { Start-Sleep -Milliseconds 500; $proc.Refresh(); $hwnd = $proc.MainWindowHandle }

$auto = [System.Windows.Automation.AutomationElement]
$root = $auto::FromHandle($hwnd)

# Aba Modelos (índice 2)
$tabCond = New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
$tabs = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
$tabs[2].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1500

$btnCond = New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
Write-Output "=== botoes na aba Modelos: $($buttons.Count) ==="
foreach ($b in $buttons) { Write-Output ("  [" + $b.Current.Name + "]") }

foreach ($b in $buttons) {
    if ($b.Current.Name -eq 'Novo documento') {
        $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        break
    }
}
Start-Sleep -Seconds 2

$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$lines = New-Object System.Collections.Generic.List[string]
function Walk($element, [int]$depth) {
    if ($depth -gt 6) { return }
    $name = $element.Current.Name
    if ($name.Length -gt 60) { $name = $name.Substring(0, 60) }
    $val = ''
    try {
        if ($element.Current.IsPassword -eq $false -and $element.GetSupportedPatterns() -contains [System.Windows.Automation.ValuePattern]::Pattern) {
            $val = ' = "' + $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value + '"'
        }
    } catch { }
    $lines.Add(('  ' * $depth) + $element.Current.ControlType.ProgrammaticName + ' | ' + $element.Current.ClassName + ' | ' + $name + $val)
    $child = $walker.GetFirstChild($element)
    while ($child -ne $null) { Walk $child ($depth + 1); $child = $walker.GetNextSibling($child) }
}
Walk $root 0
$lines | Select-Object -First 90

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
