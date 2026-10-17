$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskDownload = Join-Path $taskRoot '.downloads'
$taskSdkDir = Join-Path $taskRoot '.tools\dotnet'
$taskMpvDir = Join-Path $taskRoot '.tools\mpv'
New-Item -ItemType Directory -Path $taskDownload,$taskSdkDir,$taskMpvDir -Force | Out-Null
$ProgressPreference = 'SilentlyContinue'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools\cli'
$taskDependencies = Get-Content (Join-Path $taskRoot 'build\dependencies.json') -Raw | ConvertFrom-Json
if (-not (Test-Path (Join-Path $taskSdkDir 'dotnet.exe'))) {
    $taskSdk = $taskDependencies.dotnet
    $taskSdkZip = Join-Path $taskDownload 'dotnet-sdk.zip'
    Write-Output "Downloading .NET SDK $($taskSdk.version)"
    Invoke-WebRequest $taskSdk.url -OutFile $taskSdkZip -TimeoutSec 600
    if ((Get-FileHash $taskSdkZip -Algorithm SHA512).Hash.ToLower() -ne $taskSdk.sha512) { throw 'SDK SHA512 mismatch' }
    tar -xf $taskSdkZip -C $taskSdkDir
    if ($LASTEXITCODE -ne 0) { throw 'SDK extraction failed' }
    $taskSdk | ConvertTo-Json | Set-Content (Join-Path $taskDownload 'dotnet-provenance.json')
}
$taskMpvDll = Join-Path $taskMpvDir 'libmpv-2.dll'
if (-not (Test-Path -LiteralPath $taskMpvDll) -or (Get-FileHash -LiteralPath $taskMpvDll -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskDependencies.libmpvDllSha256) {
    $taskAsset = $taskDependencies.mpv
    $taskMpvArchive = Join-Path $taskDownload $taskAsset.name
    Write-Output "Downloading $($taskAsset.name)"
    if (-not (Test-Path -LiteralPath $taskMpvArchive) -or (Get-FileHash -LiteralPath $taskMpvArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskAsset.sha256) {
        Invoke-WebRequest $taskAsset.url -OutFile $taskMpvArchive -TimeoutSec 300
    }
    if ((Get-FileHash $taskMpvArchive -Algorithm SHA256).Hash.ToLower() -ne $taskAsset.sha256) { throw 'mpv SHA256 mismatch' }
    $taskArchive = [IO.Compression.ZipFile]::OpenRead($taskMpvArchive)
    try {
        $taskEntry = $taskArchive.GetEntry($taskAsset.entry)
        if ($null -eq $taskEntry) { throw 'libmpv DLL missing from archive' }
        $taskIncomingDll = Join-Path $taskMpvDir 'libmpv-2.dll.pending'
        try {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($taskEntry, $taskIncomingDll, $true)
            if ((Get-FileHash -LiteralPath $taskIncomingDll -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskDependencies.libmpvDllSha256) { throw 'libmpv DLL SHA256 mismatch' }
            Move-Item -LiteralPath $taskIncomingDll -Destination $taskMpvDll -Force
        } finally {
            if (Test-Path -LiteralPath $taskIncomingDll) { Remove-Item -LiteralPath $taskIncomingDll -Force }
        }
    } finally { $taskArchive.Dispose() }
    $taskAsset | ConvertTo-Json | Set-Content (Join-Path $taskDownload 'mpv-provenance.json')
}
if ((Get-FileHash -LiteralPath (Join-Path $taskMpvDir 'libmpv-2.dll') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskDependencies.libmpvDllSha256) { throw 'libmpv DLL SHA256 mismatch' }
$taskSources = Get-Content (Join-Path $taskRoot 'build/mpv-sources.json') -Raw | ConvertFrom-Json
if ($taskSources.binarySha256 -ne $taskDependencies.libmpvDllSha256) { throw 'libmpv source manifest does not match DLL' }
$taskSourceDir = Join-Path $taskMpvDir 'sources'
New-Item -ItemType Directory -Path $taskSourceDir -Force | Out-Null
foreach ($taskSource in $taskSources.files) {
    $taskSourceFile = Join-Path $taskSourceDir $taskSource.name
    if (-not (Test-Path -LiteralPath $taskSourceFile) -or (Get-FileHash -LiteralPath $taskSourceFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskSource.sha256) {
        Invoke-WebRequest $taskSource.url -OutFile $taskSourceFile -TimeoutSec 180
    }
    if ((Get-FileHash -LiteralPath $taskSourceFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskSource.sha256) { throw "Source SHA256 mismatch: $($taskSource.name)" }
}
& (Join-Path $taskSdkDir 'dotnet.exe') --version
Get-ChildItem $taskMpvDir -Filter '*mpv*.dll' -Recurse | Select-Object Name,Length
