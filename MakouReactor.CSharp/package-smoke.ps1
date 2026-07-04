param(
    [string]$PublishRoot = (Join-Path $PSScriptRoot '..\.dist\publish'),
    [string]$OutputRoot = (Join-Path ([System.IO.Path]::GetTempPath()) 'MakouReactor.PackageSmoke')
)

$ErrorActionPreference = 'Stop'

$guiPackage = Join-Path $PublishRoot 'makoureactor-gui-win64'
if (-not (Test-Path (Join-Path $guiPackage 'MakouReactor.UI.WPF.exe'))) {
    & (Join-Path $PSScriptRoot 'publish.bat')
    if ($LASTEXITCODE -ne 0) {
        throw "publish.bat failed with exit code $LASTEXITCODE"
    }
}

$extractRoot = Join-Path $OutputRoot ('gui-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
Copy-Item -Path (Join-Path $guiPackage '*') -Destination $extractRoot -Recurse -Force

$guiExe = Join-Path $extractRoot 'MakouReactor.UI.WPF.exe'
if (-not (Test-Path $guiExe)) {
    throw "GUI package is missing MakouReactor.UI.WPF.exe after extraction."
}

$process = Start-Process -FilePath $guiExe -WorkingDirectory $extractRoot -PassThru
try {
    Start-Sleep -Seconds 3
    if ($process.HasExited) {
        throw "GUI exited during smoke startup with code $($process.ExitCode)."
    }

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing

    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $screenshotPath = Join-Path $extractRoot 'package-smoke.png'
    try {
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $bitmap.Save($screenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    if ((Get-Item $screenshotPath).Length -le 10000) {
        throw "Package smoke screenshot is unexpectedly small: $screenshotPath"
    }
}
finally {
    if (-not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) {
            $process.Kill()
            $process.WaitForExit()
        }
    }
}

Write-Host "Package smoke test passed."
Write-Host "Extracted package: $extractRoot"
