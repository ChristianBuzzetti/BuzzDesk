param([switch]$Shot, [switch]$Zip, [switch]$Msix, [switch]$TestSign, [string]$Output = '')
# Compila y publica BuzzDesk autocontenido (incluye .NET 10, no requiere instalar nada).
# -Shot genera además las capturas del README en docs\ (base de datos de demostración temporal, nunca tus datos).
# -Zip arma además artifacts\BuzzDesk-<versión>-win-x64.zip para distribuir.
# -Msix arma además artifacts\store\BuzzDesk_<versión>.0_x64.msix para Microsoft Store: compilación STORE
#      (inicio con Windows por StartupTask), logos y resources.pri. La identidad sale de packaging\store.json
#      (valores de Partner Center). El .msix va sin firmar: la Store lo firma.
# -TestSign (con -Msix) deja también un ..._test.msix firmado con un certificado autofirmado del usuario actual
#      (CN = Publisher de store.json) y exporta el .cer, para instalarlo en esta PC antes de subirlo.
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $PSScriptRoot 'dist\BuzzDesk' }
# SDK: $env:BUZZDESK_DOTNET, si no 'dotnet' del PATH (.NET 10 SDK).
$dotnet = if ($env:BUZZDESK_DOTNET) { $env:BUZZDESK_DOTNET } else { 'dotnet' }
$project = Join-Path $PSScriptRoot 'src\BuzzDesk\BuzzDesk.csproj'
$running = @(Get-Process BuzzDesk -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Output, [StringComparison]::OrdinalIgnoreCase) })
if ($running.Count) { throw "BuzzDesk está abierto desde $Output. Cerralo (bandeja → Salir) antes de recompilar." }
& $dotnet publish $project -c Release -r win-x64 --self-contained true -o $Output --nologo
if ($LASTEXITCODE -ne 0) { throw 'La publicación falló.' }
Write-Output "BuzzDesk publicado en $Output"
if ($Zip) {
    $version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $zipName = 'BuzzDesk'
    $stage = Join-Path $PSScriptRoot "artifacts\zip\$zipName"
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item -Path (Join-Path $Output '*') -Destination $stage -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $stage
    $zipPath = Join-Path $PSScriptRoot "artifacts\$zipName-$version-win-x64.zip"
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
    Remove-Item -LiteralPath (Split-Path $stage) -Recurse -Force
    Write-Output ("Zip: $zipPath ({0:N0} MB)" -f ((Get-Item -LiteralPath $zipPath).Length / 1MB))
}
if ($Msix) {
    $version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $msixVersion = "$version.0"                        # la Store exige 4 partes con la última en 0
    $store = Join-Path $PSScriptRoot 'artifacts\store'
    $cache = Join-Path $PSScriptRoot 'artifacts\cache'
    $stage = Join-Path $store 'app'
    New-Item -ItemType Directory -Force $store, $cache | Out-Null
    $identity = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'packaging\store.json') -Raw | ConvertFrom-Json

    # 1. Compilación STORE.
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    & $dotnet publish $project -c Release -r win-x64 --self-contained true -o $stage --nologo -p:Store=true
    if ($LASTEXITCODE -ne 0) { throw 'La publicación STORE falló.' }

    # 2. Herramientas del Windows SDK (makeappx, makepri, signtool) desde el paquete NuGet oficial de Microsoft.
    $btVersion = '10.0.28000.2957'
    $bt = Join-Path $cache "sdk-buildtools-$btVersion"
    if (-not (Test-Path -LiteralPath (Join-Path $bt 'bin'))) {
        $nupkg = Join-Path $cache "sdk-buildtools-$btVersion.zip"
        Invoke-WebRequest -UseBasicParsing "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$btVersion/microsoft.windows.sdk.buildtools.$btVersion.nupkg" -OutFile $nupkg
        Expand-Archive -LiteralPath $nupkg -DestinationPath $bt -Force
        Remove-Item -LiteralPath $nupkg
    }
    $sdkBin = Get-ChildItem -LiteralPath (Join-Path $bt 'bin') -Directory | Sort-Object Name | Select-Object -Last 1 | ForEach-Object { Join-Path $_.FullName 'x64' }
    $makeappx = Join-Path $sdkBin 'makeappx.exe'; $makepri = Join-Path $sdkBin 'makepri.exe'; $signtool = Join-Path $sdkBin 'signtool.exe'

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $stage

    # 3. Logos (desde el ícono de la app) y manifiesto.
    Add-Type -AssemblyName System.Drawing
    $images = Join-Path $stage 'Images'
    New-Item -ItemType Directory -Force $images | Out-Null
    $src = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'src\BuzzDesk\Assets\buzzdesk.png'))
    function Save-Logo([string]$name, [int]$w, [int]$h, [double]$fill) {
        $bmp = New-Object Drawing.Bitmap $w, $h
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'
        $s = [int]([Math]::Min($w, $h) * $fill)
        $g.DrawImage($src, [int](($w - $s) / 2), [int](($h - $s) / 2), $s, $s)
        $g.Dispose(); $bmp.Save((Join-Path $images $name), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    }
    try {
        foreach ($sc in 100, 200) {
            $f = $sc / 100
            Save-Logo "Square44x44Logo.scale-$sc.png" (44 * $f) (44 * $f) 1
            Save-Logo "Square150x150Logo.scale-$sc.png" (150 * $f) (150 * $f) 0.6
            Save-Logo "Wide310x150Logo.scale-$sc.png" (310 * $f) (150 * $f) 0.6
            Save-Logo "StoreLogo.scale-$sc.png" (50 * $f) (50 * $f) 1
        }
        foreach ($t in 16, 24, 32, 48, 256) {
            Save-Logo "Square44x44Logo.targetsize-$t.png" $t $t 1
            Save-Logo "Square44x44Logo.targetsize-${t}_altform-unplated.png" $t $t 1
        }
    } finally { $src.Dispose() }
    $manifest = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'packaging\AppxManifest.xml') -Raw).
        Replace('{{IdentityName}}', $identity.IdentityName).Replace('{{Publisher}}', [Security.SecurityElement]::Escape($identity.Publisher)).
        Replace('{{PublisherDisplayName}}', [Security.SecurityElement]::Escape($identity.PublisherDisplayName)).Replace('{{Version}}', $msixVersion)
    [IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))

    # 4. resources.pri (para que Windows elija la escala y el ícono sin placa de la barra de tareas) y empaquetado.
    $priConfig = Join-Path $store 'priconfig.xml'
    & $makepri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'makepri createconfig falló.' }
    & $makepri new /pr $stage /cf $priConfig /mn (Join-Path $stage 'AppxManifest.xml') /of (Join-Path $stage 'resources.pri') /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'makepri new falló.' }
    $msixPath = Join-Path $store "BuzzDesk_${msixVersion}_x64.msix"
    & $makeappx pack /d $stage /p $msixPath /o /nv | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'makeappx pack falló.' }
    Write-Output ("MSIX (para subir a la Store): $msixPath ({0:N0} MB)" -f ((Get-Item -LiteralPath $msixPath).Length / 1MB))

    if ($TestSign) {
        $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $identity.Publisher -and $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } | Select-Object -First 1
        if (-not $cert) {
            $cert = New-SelfSignedCertificate -Type Custom -Subject $identity.Publisher -KeyUsage DigitalSignature -FriendlyName 'BuzzDesk MSIX test' `
                -CertStoreLocation Cert:\CurrentUser\My -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        }
        $testPath = Join-Path $store "BuzzDesk_${msixVersion}_x64_test.msix"
        Copy-Item -LiteralPath $msixPath $testPath -Force
        & $signtool sign /q /fd SHA256 /sha1 $cert.Thumbprint /s My $testPath
        if ($LASTEXITCODE -ne 0) { throw 'signtool falló.' }
        $cer = Join-Path $store 'BuzzDesk-test.cer'
        Export-Certificate -Cert $cert -FilePath $cer | Out-Null
        Write-Output "MSIX de prueba firmado: $testPath"
        Write-Output "Para instalarlo, una vez y como administrador: Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
    }
}
if ($Shot) {
    # Capturas del README: base de demostración en artifacts\shots\demo, interfaz en inglés; los PNG van a docs\.
    $dir = Join-Path $PSScriptRoot 'artifacts\shots'
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $p = Start-Process -FilePath (Join-Path $Output 'BuzzDesk.exe') -ArgumentList ('--shot "' + $dir + '" --lang en') -PassThru
    if (-not $p.WaitForExit(120000)) { $p.Kill(); throw 'La verificación visual no terminó en 120 s.' }
    Get-Content (Join-Path $dir 'shots.log')
    if ((Get-Content (Join-Path $dir 'shots.log') -Tail 1) -ne 'OK') { throw 'Las capturas fallaron: ver artifacts\shots\shots.log.' }
    $docs = Join-Path $PSScriptRoot 'docs'
    New-Item -ItemType Directory -Force $docs | Out-Null
    Copy-Item -Path (Join-Path $dir '*.png') -Destination $docs -Force
    Write-Output "Capturas en $docs"
}
