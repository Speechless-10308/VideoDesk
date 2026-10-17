$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path
$taskArtifacts=Join-Path $taskRoot 'artifacts'
$taskApp=Join-Path $taskArtifacts 'app'
$taskDotnet=Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
& "$PSScriptRoot/build.ps1" -Publish
$taskDependencies=Get-Content (Join-Path $taskRoot 'build/dependencies.json') -Raw | ConvertFrom-Json
if((Get-FileHash -LiteralPath (Join-Path $taskApp 'libmpv-2.dll') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskDependencies.libmpvDllSha256){throw 'Published libmpv does not match the dependency manifest'}
$taskVersion=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $taskApp 'VideoDesk.dll')).Version.ToString(3)
Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE'),(Join-Path $taskRoot 'README.md'),(Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination $taskApp
$taskZip=Join-Path $taskArtifacts 'VideoDesk-win-x64.zip'
$taskTemporaryZip=$taskZip+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
[IO.Compression.ZipFile]::CreateFromDirectory($taskApp,$taskTemporaryZip,[IO.Compression.CompressionLevel]::Optimal,$false)
[IO.File]::Move($taskTemporaryZip,$taskZip,$true)
(Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath (Join-Path $taskArtifacts 'client-payload.sha256') -Encoding ascii
$taskSetup=Join-Path $taskArtifacts 'setup.pending'
Push-Location $taskRoot
try{
    $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
    $env:DOTNET_CLI_HOME=Join-Path $taskRoot '.tools\cli'
    & $taskDotnet publish src/VideoDesk.Setup -c Release -r win-x64 --self-contained true -p:Version=$taskVersion -o $taskSetup --nologo -p:UseSharedCompilation=false
    if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
    Copy-Item -LiteralPath (Join-Path $taskSetup 'VideoDeskSetup.exe') -Destination (Join-Path $taskArtifacts 'VideoDesk-Setup-win-x64.exe') -Force
    $taskAbsoluteSetup=[IO.Path]::GetFullPath($taskSetup)
    if(-not $taskAbsoluteSetup.StartsWith([IO.Path]::GetFullPath($taskArtifacts)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid installer staging directory'}
    if(@(Get-ChildItem -LiteralPath $taskAbsoluteSetup -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Installer staging contains links'}
    Remove-Item -LiteralPath $taskAbsoluteSetup -Recurse -Force
    [ordered]@{version=$taskVersion;platform='win-x64';selfContained=$true;app='artifacts/app/VideoDesk.exe';zip='artifacts/VideoDesk-win-x64.zip';setup='artifacts/VideoDesk-Setup-win-x64.exe';appSha256=(Get-FileHash -LiteralPath (Join-Path $taskApp 'VideoDesk.dll')).Hash.ToLowerInvariant();coreSha256=(Get-FileHash -LiteralPath (Join-Path $taskApp 'VideoDesk.Core.dll')).Hash.ToLowerInvariant();zipBytes=(Get-Item -LiteralPath $taskZip).Length;setupBytes=(Get-Item -LiteralPath (Join-Path $taskArtifacts 'VideoDesk-Setup-win-x64.exe')).Length;packagedAt=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskArtifacts 'build-manifest.json') -Encoding utf8
    Write-Output "Current release $taskVersion packaged."
}finally{Pop-Location}

