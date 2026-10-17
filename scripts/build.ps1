param([switch]$Publish)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
Push-Location $taskRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
    $env:DOTNET_CLI_HOME=Join-Path $taskRoot '.tools\cli'
    $taskDotnet=Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
    & "$PSScriptRoot\bootstrap.ps1"
    if($Publish){& $taskDotnet publish src\VideoDesk.App -c Release -r win-x64 --self-contained true -o artifacts\app --nologo -p:UseSharedCompilation=false}
    else{& $taskDotnet build VideoDesk.slnx -c Release --nologo -p:UseSharedCompilation=false}
    if($LASTEXITCODE -ne 0){throw 'Build failed'}
}finally{Pop-Location}






