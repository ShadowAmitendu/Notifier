# Ensure System.Drawing is loaded
Add-Type -AssemblyName System.Drawing

$scriptDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptDir)) { $scriptDir = Get-Location }

Write-Host "Synchronizing version from Notifier.csproj to Package.appxmanifest..." -ForegroundColor Cyan
$csprojPath = "$scriptDir\Notifier.csproj"
$manifestPath = "$scriptDir\Package.appxmanifest"

if (Test-Path $csprojPath) {
    [xml]$csproj = Get-Content -Path $csprojPath -Raw
    $versionNode = $csproj.SelectSingleNode("//Version")
    if ($null -ne $versionNode) {
        $version = $versionNode.InnerText.Trim()
        Write-Host "Found version $version in Notifier.csproj" -ForegroundColor Green
        
        # MSIX versions require 4 parts (e.g. 1.0.0.0)
        $parts = $version.Split('.')
        while ($parts.Count -lt 4) {
            $parts += "0"
        }
        $appxVersion = ($parts[0..3] -join ".")
        
        if (Test-Path $manifestPath) {
            [xml]$manifest = Get-Content -Path $manifestPath -Raw
            $manifest.Package.Identity.Version = $appxVersion
            $manifest.Save($manifestPath)
            Write-Host "Updated Package.appxmanifest identity version to $appxVersion" -ForegroundColor Green
        } else {
            Write-Warning "Package.appxmanifest not found at $manifestPath"
        }
    } else {
        Write-Warning "Could not find <Version> tag in Notifier.csproj"
    }
} else {
    Write-Warning "Notifier.csproj not found at $csprojPath"
}

Write-Host "Creating Assets directory and generating logo images..." -ForegroundColor Cyan

$assetsDir = "$scriptDir\Assets"
if (!(Test-Path $assetsDir)) {
    New-Item -ItemType Directory -Path $assetsDir | Out-Null
}

function New-Logo($path, $width, $height) {
    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb(2, 132, 199)) # Sky-600 Blue

    $penWidth = $width * 0.12
    if ($penWidth -lt 2) { $penWidth = 2 }
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $penWidth
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $x1 = [int]($width * 0.28)
    $x2 = [int]($width * 0.72)
    $y1 = [int]($height * 0.25)
    $y2 = [int]($height * 0.75)

    # Draw geometric 'N'
    $g.DrawLine($pen, $x1, $y1, $x1, $y2)
    $g.DrawLine($pen, $x1, $y1, $x2, $y2)
    $g.DrawLine($pen, $x2, $y1, $x2, $y2)

    $pen.Dispose()
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

New-Logo "$assetsDir\Square150x150Logo.png" 150 150
New-Logo "$assetsDir\Square44x44Logo.png" 44 44
New-Logo "$assetsDir\Wide310x150Logo.png" 310 150
New-Logo "$assetsDir\StoreLogo.png" 50 50
New-Logo "$assetsDir\SplashScreen.png" 620 300

Write-Host "Building and publishing packaged MSIX..." -ForegroundColor Cyan

# Remove old msix files if any to avoid confusion
Get-ChildItem -Path $scriptDir -Filter "*.msix" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

# Run publish command
dotnet publish $csprojPath -c Release -r win-x64 -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false --self-contained

# Find generated MSIX
$msixFile = Get-ChildItem -Path $scriptDir -Filter "*.msix" -Recurse | Select-Object -First 1

if ($null -eq $msixFile) {
    Write-Error "MSIX Package generation failed. No .msix file found in build output."
    exit 1
}

$msixPath = $msixFile.FullName
Write-Host "Generated MSIX Package at: $msixPath" -ForegroundColor Green

Write-Host "Creating self-signed Code Signing Certificate..." -ForegroundColor Cyan

# Create certificate for publisher CN=Amitendu
$certSubject = "Amitendu Bikash Dhusiya (Shadow Amitendu)"
$cert = New-SelfSignedCertificate -Type Custom -Subject $certSubject -HashAlgorithm SHA256 -KeyUsage DigitalSignature -FriendlyName "Notifier Publisher" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")

if ($null -eq $cert) {
    Write-Error "Failed to create self-signed certificate."
    exit 1
}

Write-Host "Signing the MSIX installer..." -ForegroundColor Cyan
$signtool = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Filter "signtool.exe" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*\x64\*" } | Select-Object -First 1
if ($null -eq $signtool) {
    $signtool = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages" -Filter "signtool.exe" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*\x64\*" } | Select-Object -First 1
}
if ($null -eq $signtool) {
    Write-Error "signtool.exe not found in NuGet cache. Signing failed."
    exit 1
}
$signtoolPath = $signtool.FullName
Write-Host "Using SignTool: $signtoolPath" -ForegroundColor Green

& $signtoolPath sign /sha1 $($cert.Thumbprint) /fd sha256 /v $msixPath

Write-Host "Exporting publisher certificate to Installer folder..." -ForegroundColor Cyan
$installerFolder = "$scriptDir\Installer"
if (!(Test-Path $installerFolder)) {
    New-Item -ItemType Directory -Path $installerFolder | Out-Null
}

$cerPath = "$installerFolder\NotifierPublisher.cer"
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

# Copy MSIX to the Installer folder for easy access
$msixDestPath = "$installerFolder\SiteUpdateNotifier.msix"
Copy-Item -Path $msixPath -Destination $msixDestPath -Force

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "MSIX Package Created and Signed Successfully!" -ForegroundColor Green
Write-Host "Installer: $msixDestPath" -ForegroundColor Green
Write-Host "Certificate: $cerPath" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
Write-Host "`nTo install it on any machine (GUI method):" -ForegroundColor Yellow
Write-Host "1. Double-click 'NotifierPublisher.cer'" -ForegroundColor Yellow
Write-Host "2. Click 'Install Certificate...'" -ForegroundColor Yellow
Write-Host "3. Select 'Local Machine' and click Next" -ForegroundColor Yellow
Write-Host "4. Select 'Place all certificates in the following store', click Browse, and choose 'Trusted Root Certification Authorities' (required for sideloading self-signed MSIX packages)" -ForegroundColor Yellow
Write-Host "5. Complete the import wizard" -ForegroundColor Yellow
Write-Host "6. Now double-click 'SiteUpdateNotifier.msix' to install the app!" -ForegroundColor Yellow
Write-Host "`nOr run these commands in an Administrator PowerShell window to trust it instantly:" -ForegroundColor Yellow
Write-Host "Import-Certificate -FilePath `"$installerFolder\NotifierPublisher.cer`" -CertStoreLocation Cert:\LocalMachine\Root" -ForegroundColor Cyan
Write-Host "Import-Certificate -FilePath `"$installerFolder\NotifierPublisher.cer`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople" -ForegroundColor Cyan
