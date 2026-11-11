Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = 'D:\Documentos\projects\MiniPrint-Desktop\src\MiniPrint.Desktop\bin\Debug\net8.0-windows10.0.19041.0\MiniPrint.exe'
$proc = Start-Process -FilePath $exe -PassThru
$hwnd = 0
for ($i = 0; $i -lt 30 -and $hwnd -eq 0; $i++) { Start-Sleep -Milliseconds 500; $proc.Refresh(); $hwnd = $proc.MainWindowHandle }

$auto = [System.Windows.Automation.AutomationElement]
$root = $auto::FromHandle($hwnd)

$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$lines = New-Object System.Collections.Generic.List[string]

function Walk($element, [int]$depth) {
    if ($depth -gt 7) { return }
    $indent = '  ' * $depth
    $name = $element.Current.Name
    if ($name.Length -gt 70) { $name = $name.Substring(0, 70) }
    $lines.Add(("{0}{1} | {2} | {3}" -f $indent, $element.Current.ControlType.ProgrammaticName, $element.Current.ClassName, $name))
    $child = $walker.GetFirstChild($element)
    while ($child -ne $null) {
        Walk $child ($depth + 1)
        $child = $walker.GetNextSibling($child)
    }
}

Walk $root 0
$lines | Select-Object -First 120
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
