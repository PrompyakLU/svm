# Build + test the API and print only the lines that matter.
# Usage (from repo root): powershell -NoProfile -ExecutionPolicy Bypass -File api\verify.ps1
$ErrorActionPreference = 'Continue'
$api = $PSScriptRoot
$sln = Join-Path $api 'SvmApi.slnx'

dotnet build $sln -nologo -v q --tl:off 2>&1 |
    ForEach-Object { "$_" } |
    Where-Object { $_ -match ': error |Build succeeded|Build FAILED|Error\(s\)' } |
    Select-Object -Unique
if ($LASTEXITCODE -ne 0) { Write-Host 'BUILD FAILED - paste the error lines above to Kiro'; exit 1 }

$lines = dotnet test $sln --no-build -nologo --tl:off --logger "console;verbosity=normal" 2>&1 |
    ForEach-Object { "$_" }
$testExit = $LASTEXITCODE

# Failure details only: never print "Passed ..." lines. Case-sensitive so Theory args like "expected:" don't match.
$lines |
    Where-Object { $_ -notmatch '^\s*Passed ' } |
    Where-Object { $_ -cmatch '^\s*Failed |\[FAIL\]|Error Message|Assert\.|Expected:|Actual:|\.cs:line \d+' } |
    Select-Object -First 30

# Summary line always last.
$lines | Where-Object { $_ -cmatch 'Passed!|Failed!' }

if ($testExit -ne 0) { Write-Host 'TESTS FAILED - paste the lines above to Kiro'; exit 1 }
Write-Host 'ALL GOOD'
