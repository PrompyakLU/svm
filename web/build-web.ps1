$ErrorActionPreference = "Continue"
$env:Path = "$([Environment]::GetEnvironmentVariable('Path','Machine'));$([Environment]::GetEnvironmentVariable('Path','User'))"
$env:TEMP = "D:\KiroWorkshop\.tmp"
$env:TMP = "D:\KiroWorkshop\.tmp"
$env:NG_CLI_ANALYTICS = "false"
Set-Location "D:\KiroWorkshop\web"
Write-Output "BUILD_STARTING"
& "D:\KiroWorkshop\web\node_modules\.bin\ng.cmd" build --configuration development
Write-Output "WAVE2_BUILD_DONE exit=$LASTEXITCODE"
