$ErrorActionPreference = "Continue"
$env:Path = "$([Environment]::GetEnvironmentVariable('Path','Machine'));$([Environment]::GetEnvironmentVariable('Path','User'))"
$env:TEMP = "D:\KiroWorkshop\.tmp"
$env:TMP = "D:\KiroWorkshop\.tmp"
$env:NG_CLI_ANALYTICS = "false"
Set-Location "D:\KiroWorkshop\web"
& "D:\KiroWorkshop\web\node_modules\.bin\ng.cmd" serve --port 4200 --host 127.0.0.1
