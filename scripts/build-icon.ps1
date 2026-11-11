# Deterministic vector rendering; transparent PNG previews and a multi-resolution Windows ICO.
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskAssets = Join-Path $taskRoot 'assets'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$taskSvg = [xml](Get-Content -LiteralPath (Join-Path $taskAssets 'app-icon.svg') -Raw)
$taskItems = @($taskSvg.svg.ChildNodes | Where-Object { $_.LocalName -in @('path','rect') })
$taskImages = @()
$taskSizes = @(16,20,24,32,40,48,64,128,256)
foreach ($taskSize in $taskSizes) {
    $taskVisual = New-Object System.Windows.Media.DrawingVisual
    $taskDrawing = $taskVisual.RenderOpen()
    $taskDrawing.PushTransform((New-Object System.Windows.Media.ScaleTransform(($taskSize/256.0),($taskSize/256.0))))
    foreach ($taskItem in $taskItems) {
        $taskBrush = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.ColorConverter]::ConvertFromString($taskItem.GetAttribute('fill')))
        if ($taskItem.LocalName -eq 'path') {
            $taskDrawing.DrawGeometry($taskBrush,$null,[System.Windows.Media.Geometry]::Parse($taskItem.GetAttribute('d')))
        } else {
            $taskRect = New-Object System.Windows.Rect([double]$taskItem.x,[double]$taskItem.y,[double]$taskItem.width,[double]$taskItem.height)
            $taskDrawing.DrawRoundedRectangle($taskBrush,$null,$taskRect,[double]$taskItem.rx,[double]$taskItem.rx)
        }
    }
    $taskDrawing.Pop()
    $taskDrawing.Close()
    $taskBitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($taskSize,$taskSize,96,96,[System.Windows.Media.PixelFormats]::Pbgra32)
    $taskBitmap.Render($taskVisual)
    $taskEncoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $taskEncoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($taskBitmap))
    $taskStream = New-Object System.IO.MemoryStream
    $taskEncoder.Save($taskStream)
    $taskBytes = $taskStream.ToArray()
    $taskStream.Dispose()
    [System.IO.File]::WriteAllBytes((Join-Path $taskAssets ('app-icon-' + $taskSize + '.png')),$taskBytes)
    $taskImages += ,$taskBytes
}
$taskFile = [System.IO.File]::Create((Join-Path $taskAssets 'app-icon.ico'))
$taskWriter = New-Object System.IO.BinaryWriter($taskFile)
try {
    $taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]$taskSizes.Count)
    $taskOffset = 6 + 16 * $taskSizes.Count
    for ($taskI=0; $taskI -lt $taskSizes.Count; $taskI++) {
        $taskDim = if ($taskSizes[$taskI] -eq 256) { 0 } else { $taskSizes[$taskI] }
        $taskWriter.Write([byte]$taskDim); $taskWriter.Write([byte]$taskDim)
        $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
        $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32)
        $taskWriter.Write([uint32]$taskImages[$taskI].Length); $taskWriter.Write([uint32]$taskOffset)
        $taskOffset += $taskImages[$taskI].Length
    }
    foreach ($taskImage in $taskImages) { $taskWriter.Write([byte[]]$taskImage) }
} finally { $taskWriter.Dispose() }
Write-Output ('Icon built: ' + (Join-Path $taskAssets 'app-icon.ico'))
