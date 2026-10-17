$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskExe=Join-Path $taskRoot 'artifacts\app\VideoDesk.exe'
if(-not(Test-Path $taskExe)){& "$PSScriptRoot\build.ps1" -Publish}
Start-Process -FilePath $taskExe -ArgumentList "--root=`"$taskRoot`"" -WindowStyle Hidden






