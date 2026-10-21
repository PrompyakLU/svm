<#
.SYNOPSIS
  Wave 4 end-to-end smoke test of the SVM API over HTTP
  (auth, assets, reference, intake, risk, workflow, assignment/evidence/comments/notifications, SLA, verification, audit).

.DESCRIPTION
  Run:  powershell -NoProfile -ExecutionPolicy Bypass -File api\smoke-wave4.ps1
  Optional: -BaseUrl http://host:port   test an already running API (nothing is started/stopped)
            -Port 5199                  preferred local port when the script starts the API itself
            -Password <demo password>   last-resort source of the demo password
            -Only <module[,module]>     all | health | auth | reference | assets | intake | risk | workflow |
                                        assignment | sla | verification | audit
                                        (modules that the selected one depends on run silently first)

  PREREQUISITES (the script does not set these up):
    1. SQL Server container is up and the migrations are applied to the dev database.
    2. `dotnet build` of the api solution was done (the API is started with `dotnet run --no-build`).
    3. user-secrets of src\SvmApi.Api contain Seed:DemoPassword, Auth:Jwt:SigningKey and the
       ConnectionStrings:Svm password (Seed:Enabled is true in appsettings.Development.json).
       The demo password is read from user-secrets, else env var Seed__DemoPassword, else -Password.
       It is never printed. JWTs, cookies, CSRF tokens and connection strings are never printed either.

  SIDE EFFECTS - READ THIS: the script writes REAL rows into the local DEV database: findings, import batches,
  vulnerability instances, risk/status history, remediation tasks, evidence files, comments, notifications,
  verification records and audit rows. Names carry a timestamp suffix so reruns do not collide.
  It never deletes or edits pre-existing data and calls no destructive endpoint.

  Output: one line per check (PASS|FAIL|SKIP), then "TOTAL n  PASS p  FAIL f  SKIP s" and one line per FAIL.
  Exit code 0 when there is no FAIL, else 1.

  Notes: POST /auth/login is rate limited (default 10/min per IP); the script uses ~9 logins and waits and
  retries on HTTP 429. Access tokens live 5 minutes, so sessions are re-created after 4 minutes.
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = '',
    [int]$Port = 5199,
    [string]$Password = '',
    [ValidateSet('all', 'health', 'auth', 'reference', 'assets', 'intake', 'risk', 'workflow', 'assignment', 'sla', 'verification', 'audit')]
    [string[]]$Only = @('all')
)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$script:Results = New-Object System.Collections.ArrayList
$script:Quiet = $false
$script:CurModule = ''
$script:LastHeader = ''
$script:Sessions = @{}
$script:Pw = ''
$script:Base = ''
$script:Anon = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$script:Ctx = @{ Ts = (Get-Date).ToString('yyyyMMddHHmmss'); A = $null; O = $null }
$script:ApiProj = Join-Path $PSScriptRoot 'src\SvmApi.Api'

# ----------------------------------------------------------------------------- reporting

function Add-Result([string]$Status, [string]$Name, [string]$Exp, [string]$Act, [string]$Code, [string]$Why) {
    if ($script:Quiet) { return }
    $rec = [pscustomobject]@{ Module = $script:CurModule; Status = $Status; Name = $Name; Exp = $Exp; Act = $Act; Code = $Code; Why = $Why }
    [void]$script:Results.Add($rec)
    if ($script:LastHeader -ne $script:CurModule) {
        $script:LastHeader = $script:CurModule
        Write-Host ('-- ' + $script:CurModule) -ForegroundColor DarkGray
    }
    $codeText = ''
    if ($Code) { $codeText = ' [' + $Code + ']' }
    if ($Status -eq 'PASS') {
        Write-Host ('PASS  {0}  expected={1} actual={2}{3}' -f $Name, $Exp, $Act, $codeText) -ForegroundColor Green
    }
    elseif ($Status -eq 'FAIL') {
        $whyText = ''
        if ($Why) { $whyText = '  (' + $Why + ')' }
        Write-Host ('FAIL  {0}  expected={1} actual={2}{3}{4}' -f $Name, $Exp, $Act, $codeText, $whyText) -ForegroundColor Red
    }
    else {
        Write-Host ('SKIP  {0}  ({1})' -f $Name, $Why) -ForegroundColor Yellow
    }
}

function Fake([int]$Status, [string]$Code = '') {
    return [pscustomobject]@{ Status = $Status; Body = $null; Code = $Code }
}

# Check: PASS when the HTTP status equals $Exp, the optional problem code matches and the extra condition holds.
function Check([string]$Name, $Resp, [int]$Exp, [string]$ExpCode = '', [bool]$Cond = $true, [string]$Why = '') {
    $act = [int]$Resp.Status
    $ok = ($act -eq $Exp)
    $note = ''
    if (-not $ok) {
        if ($ExpCode) { $note = 'want code ' + $ExpCode }
    }
    elseif ($ExpCode -and ($Resp.Code -ne $ExpCode)) {
        $ok = $false
        $note = 'want code ' + $ExpCode
    }
    elseif (-not $Cond) {
        $ok = $false
        $note = $Why
        if (-not $note) { $note = 'condition not met' }
    }
    $verdict = 'FAIL'
    if ($ok) { $verdict = 'PASS' }
    Add-Result $verdict $Name ([string]$Exp) ([string]$act) ([string]$Resp.Code) $note
}

function Skip([string]$Name, [string]$Reason) {
    Add-Result 'SKIP' $Name '' '' '' $Reason
}

function Cnt($x) {
    if ($null -eq $x) { return 0 }
    return @($x).Count
}

# ----------------------------------------------------------------------------- HTTP

function Send-Http {
    param(
        [string]$Method,
        [string]$Path,
        $Session,
        [hashtable]$Headers,
        $BodyBytes,
        [string]$ContentType,
        [int]$Timeout = 30
    )
    $p = @{
        Uri                = ($script:Base + $Path)
        Method             = $Method
        UseBasicParsing    = $true
        TimeoutSec         = $Timeout
        ErrorAction        = 'Stop'
        MaximumRedirection = 0
    }
    if ($Session) { $p['WebSession'] = $Session }
    if ($Headers -and $Headers.Count -gt 0) { $p['Headers'] = $Headers }
    if ($null -ne $BodyBytes) {
        $p['Body'] = $BodyBytes
        if ($ContentType) { $p['ContentType'] = $ContentType }
    }

    $status = 0
    $text = ''
    try {
        $r = Invoke-WebRequest @p
        $status = [int]$r.StatusCode
        if ($r.Content -is [string]) { $text = $r.Content }
    }
    catch {
        $ex = $_.Exception
        $resp = $ex.Response
        if ($null -ne $resp) {
            $status = [int]$resp.StatusCode
            if ($resp -is [System.Net.HttpWebResponse]) {
                try {
                    $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
                    $text = $sr.ReadToEnd()
                    $sr.Close()
                }
                catch { $text = '' }
            }
            else {
                $text = [string]$_.ErrorDetails.Message
            }
        }
    }

    $body = $null
    $code = ''
    if ($text) {
        $t = $text.TrimStart([char]0xFEFF).TrimStart()
        if ($t.StartsWith('{') -or $t.StartsWith('[')) {
            try { $body = ConvertFrom-Json -InputObject $t } catch { $body = $null }
        }
    }
    if ($null -ne $body -and $body.PSObject.Properties['code']) { $code = [string]$body.code }
    return [pscustomobject]@{ Status = $status; Body = $body; Code = $code }
}

function New-Multipart([string]$FileName, [byte[]]$Bytes, [hashtable]$Fields, [string]$FileContentType) {
    $boundary = '----smoke' + [guid]::NewGuid().ToString('N')
    $ms = New-Object System.IO.MemoryStream
    $enc = New-Object System.Text.UTF8Encoding($false)
    foreach ($k in $Fields.Keys) {
        $part = '--' + $boundary + "`r`n" + 'Content-Disposition: form-data; name="' + $k + '"' + "`r`n`r`n" + [string]$Fields[$k] + "`r`n"
        $b = $enc.GetBytes($part)
        $ms.Write($b, 0, $b.Length)
    }
    $head = '--' + $boundary + "`r`n" + 'Content-Disposition: form-data; name="file"; filename="' + $FileName + '"' + "`r`n" +
    'Content-Type: ' + $FileContentType + "`r`n`r`n"
    $hb = $enc.GetBytes($head)
    $ms.Write($hb, 0, $hb.Length)
    $ms.Write($Bytes, 0, $Bytes.Length)
    $tb = $enc.GetBytes("`r`n--" + $boundary + "--`r`n")
    $ms.Write($tb, 0, $tb.Length)
    return [pscustomobject]@{ Bytes = $ms.ToArray(); ContentType = ('multipart/form-data; boundary=' + $boundary) }
}

function Get-Csrf($Sess) {
    $c = Send-Http 'GET' '/api/v1/auth/csrf' $Sess @{} $null ''
    if ($c.Status -eq 200 -and $c.Body -and $c.Body.token) { return [string]$c.Body.token }
    return ''
}

function Post-Login($Sess, [string]$Csrf, [string]$User, [string]$Pw) {
    $json = ConvertTo-Json -InputObject @{ username = $User; password = $Pw } -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $r = $null
    for ($i = 0; $i -lt 5; $i++) {
        $r = Send-Http 'POST' '/api/v1/auth/login' $Sess @{ 'X-XSRF-TOKEN' = $Csrf } $bytes 'application/json; charset=utf-8'
        if ($r.Status -ne 429) { break }
        Start-Sleep -Seconds 15   # login rate limit window is 60 s
    }
    return $r
}

function New-UserSession([string]$User) {
    $sess = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $s = [pscustomobject]@{ Name = $User; Sess = $sess; Csrf = ''; Token = ''; UserId = ''; At = [datetime]::MinValue; LoginStatus = 0; LoginCode = '' }
    $csrf = Get-Csrf $sess
    if (-not $csrf) {
        $s.LoginCode = 'CSRF_UNAVAILABLE'
        return $s
    }
    $s.Csrf = $csrf
    $r = Post-Login $sess $csrf $User $script:Pw
    $s.LoginStatus = [int]$r.Status
    $s.LoginCode = [string]$r.Code
    if ($r.Status -eq 200 -and $r.Body -and $r.Body.token) {
        $s.Token = [string]$r.Body.token
        $s.UserId = [string]$r.Body.user.id
        $s.At = Get-Date
    }
    return $s
}

function Get-Sess([string]$User) {
    $s = $script:Sessions[$User]
    if ($s -and $s.Token -and (((Get-Date) - $s.At).TotalSeconds -lt 240)) { return $s }
    $s = New-UserSession $User
    $script:Sessions[$User] = $s
    return $s
}

# Api <METHOD> <path> [user] [json body hashtable] [multipart]
function Api([string]$Method, [string]$Path, [string]$User = '', $Body = $null, $Multipart = $null) {
    $hdr = @{ 'Accept' = 'application/json' }
    $sess = $script:Anon
    if ($User) {
        $s = Get-Sess $User
        if (-not $s.Token) { return (Fake -Status -1 -Code 'LOGIN_FAILED') }
        $sess = $s.Sess
        $hdr['Authorization'] = 'Bearer ' + $s.Token
        if ($Method -ne 'GET') { $hdr['X-XSRF-TOKEN'] = $s.Csrf }
    }
    $bytes = $null
    $ct = ''
    if ($null -ne $Multipart) {
        $bytes = $Multipart.Bytes
        $ct = $Multipart.ContentType
    }
    elseif ($null -ne $Body) {
        $json = ConvertTo-Json -InputObject $Body -Compress -Depth 6
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
        $ct = 'application/json; charset=utf-8'
    }
    return (Send-Http $Method $Path $sess $hdr $bytes $ct)
}

# ----------------------------------------------------------------------------- scenario helpers

function Get-ManualBody([string]$Tag, [string]$Asset, [string]$Cve, [string]$Sev, [int]$PortNo) {
    $ts = $script:Ctx.Ts
    return @{
        assetCode      = $Asset
        sourceSystem   = 'SmokeTest'
        sourceRecordId = "SMK-${Tag}-${ts}"
        findingName    = "Smoke finding ${Tag} ${ts}"
        severity       = $Sev
        cveId          = $Cve
        location       = "https://smoke-${Tag}-${ts}.svm.example/path"
        port           = $PortNo
        component      = "smoke-${Tag}-${ts}"
    }
}

function Do-Transition([string]$User, [string]$Id, [string]$To, [string]$Rv, [string]$Reason = '') {
    $b = @{ toStatus = $To; rowVersion = $Rv }
    if ($Reason) { $b['reason'] = $Reason }
    return (Api 'POST' "/api/v1/instances/$Id/transition" $User $b)
}

# copies a fresh rowVersion from a response into the scenario object; returns $true when found
function Update-Rv($Scn, $Resp) {
    if ($Resp.Status -eq 200 -and $Resp.Body -and $Resp.Body.rowVersion) {
        $Scn.Rv = [string]$Resp.Body.rowVersion
        return $true
    }
    return $false
}

function Get-EicarBytes {
    # assembled at runtime from parts so antivirus does not flag this script file; never written to disk
    $parts = @('X5O!P%@AP[4\PZX54', '(P^)7CC)7}$EICAR', '-STANDARD-ANTIVIRUS', '-TEST-FILE!$H+H*')
    return [System.Text.Encoding]::ASCII.GetBytes(($parts -join ''))
}

function Send-Evidence([string]$User, [string]$Id, [string]$FileName, [byte[]]$Bytes) {
    $mp = New-Multipart $FileName $Bytes @{} 'text/plain'
    return (Api 'POST' "/api/v1/instances/$Id/evidence" $User $null $mp)
}

function Get-CleanEvidenceBytes([string]$Tag) {
    return [System.Text.Encoding]::UTF8.GetBytes("Smoke test evidence ${Tag} " + $script:Ctx.Ts + "`r`nGenerated by smoke-wave4.ps1`r`n")
}

function Find-AssetId([string]$Code) {
    $r = Api 'GET' ('/api/v1/assets?pageSize=10&q=' + $Code) 'officer1'
    if ($r.Status -eq 200 -and $r.Body) {
        foreach ($it in @($r.Body.items)) {
            if ($it -and [string]$it.assetCode -eq $Code) { return [string]$it.id }
        }
    }
    return ''
}

# create -> recalc -> UnderReview -> Confirmed -> assign remediation1 -> Assigned -> InProgress (remediation1)
function Drive-ToInProgress([string]$Tag, [string]$Cve, [string]$Sev, [int]$PortNo) {
    $res = [pscustomobject]@{ Ok = $false; Step = ''; Last = $null; Id = ''; Rv = '' }
    $r = Api 'POST' '/api/v1/findings/manual' 'analyst1' (Get-ManualBody $Tag 'SVM-SRV-001' $Cve $Sev $PortNo)
    $res.Last = $r
    if ($r.Status -ne 201 -or -not $r.Body -or $r.Body.outcome -ne 'Created' -or -not $r.Body.instanceId) { $res.Step = 'create finding'; return $res }
    $id = [string]$r.Body.instanceId
    $res.Id = $id

    $r = Api 'POST' "/api/v1/instances/$id/recalculate" 'analyst1' @{}
    $res.Last = $r
    if ($r.Status -ne 200 -or -not $r.Body.rowVersion) { $res.Step = 'recalculate'; return $res }
    $rv = [string]$r.Body.rowVersion

    foreach ($st in @('UnderReview', 'Confirmed')) {
        $r = Do-Transition 'officer1' $id $st $rv
        $res.Last = $r
        if ($r.Status -ne 200 -or -not $r.Body.rowVersion) { $res.Step = 'transition ' + $st; return $res }
        $rv = [string]$r.Body.rowVersion
    }

    $remId = (Get-Sess 'remediation1').UserId
    $r = Api 'POST' "/api/v1/instances/$id/assign" 'officer1' @{ assignToUserId = $remId }
    $res.Last = $r
    if ($r.Status -ne 200 -or -not $r.Body.rowVersion) { $res.Step = 'assign'; return $res }
    $rv = [string]$r.Body.rowVersion

    $r = Do-Transition 'officer1' $id 'Assigned' $rv
    $res.Last = $r
    if ($r.Status -ne 200 -or -not $r.Body.rowVersion) { $res.Step = 'transition Assigned'; return $res }
    $rv = [string]$r.Body.rowVersion

    $r = Do-Transition 'remediation1' $id 'InProgress' $rv
    $res.Last = $r
    if ($r.Status -ne 200 -or -not $r.Body.rowVersion) { $res.Step = 'transition InProgress'; return $res }
    $res.Rv = [string]$r.Body.rowVersion
    $res.Ok = $true
    return $res
}

function Check-Setup([string]$Name, $Scn) {
    $resp = $Scn.Last
    if ($Scn.Ok) { $resp = Fake -Status 200 }
    Check $Name $resp 200 '' $Scn.Ok ('failed at step: ' + $Scn.Step)
}

# ============================================================================= modules

function Test-Health {
    $r = Api 'GET' '/health' ''
    Check 'GET /health' $r 200 '' ([bool]($r.Body -and $r.Body.status -eq 'ok')) 'status not ok'

    $r = Api 'GET' '/api/v1/auth/me' ''
    Check 'GET /auth/me without token' $r 401

    # wrong password on a throwaway anonymous session (CSRF first, as the real client does)
    $sess = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $csrf = Get-Csrf $sess
    if (-not $csrf) {
        Check 'POST /auth/login wrong password' (Fake -Status 0 -Code 'CSRF_UNAVAILABLE') 401
    }
    else {
        $r = Post-Login $sess $csrf 'officer1' ('wrong-' + [guid]::NewGuid().ToString('N'))
        Check 'POST /auth/login wrong password' $r 401
    }
}

function Test-Auth {
    foreach ($u in @('officer1', 'analyst1', 'assetowner1', 'remediation1', 'approver1', 'auditor1', 'exec1')) {
        $s = Get-Sess $u
        Check "login $u" (Fake -Status $s.LoginStatus -Code $s.LoginCode) 200 '' ([bool]$s.Token) 'no token in response'
    }

    $r = Api 'GET' '/api/v1/auth/me' 'officer1'
    Check 'GET /auth/me (officer1)' $r 200 '' ([bool]($r.Body -and $r.Body.user.username -eq 'officer1')) 'username mismatch'

    # separate throwaway session so that the logout does not kill the sessions used by later modules
    $t = New-UserSession 'officer1'
    Check 'login throwaway session' (Fake -Status $t.LoginStatus -Code $t.LoginCode) 200 '' ([bool]$t.Token) 'no token in response'
    if ($t.Token) {
        $out = Send-Http 'POST' '/api/v1/auth/logout' $t.Sess @{ 'Authorization' = ('Bearer ' + $t.Token); 'X-XSRF-TOKEN' = $t.Csrf } $null ''
        Check 'POST /auth/logout' $out 204
        $me = Send-Http 'GET' '/api/v1/auth/me' $t.Sess @{ 'Authorization' = ('Bearer ' + $t.Token) } $null ''
        Check 'GET /auth/me after logout' $me 401
    }
    else {
        Skip 'logout / me after logout' 'throwaway login failed'
    }
}

function Test-Reference {
    $r = Api 'GET' '/api/v1/reference/cve?pageSize=100' 'analyst1'
    Check 'analyst1 GET cve list' $r 200 '' ([bool]($r.Body -and [long]$r.Body.total -ge 11)) 'total < 11'

    $r = Api 'GET' '/api/v1/reference/cve/CVE-2021-44228' 'analyst1'
    Check 'analyst1 GET cve CVE-2021-44228' $r 200 '' ([bool]($r.Body -and $r.Body.isKev -eq $true)) 'isKev not true'

    $r = Api 'GET' '/api/v1/reference/cve' 'exec1'
    Check 'exec1 GET cve list' $r 403

    $r = Api 'GET' '/api/v1/reference/cve/CVE-1999-0001' 'analyst1'
    Check 'analyst1 GET unknown CVE' $r 404 'REFERENCE_NOT_FOUND'
}

function Test-Assets {
    $o = Api 'GET' '/api/v1/assets?pageSize=100' 'officer1'
    $oTotal = [long]0
    if ($o.Body -and $null -ne $o.Body.total) { $oTotal = [long]$o.Body.total }
    Check 'officer1 GET assets' $o 200 '' ([bool]($oTotal -gt 0)) 'no assets'

    $ao = Api 'GET' '/api/v1/assets?pageSize=100' 'assetowner1'
    $allowed = @('Digital Channels', 'IT Infrastructure')
    $bad = 0
    $aoTotal = [long]0
    if ($ao.Body) {
        if ($null -ne $ao.Body.total) { $aoTotal = [long]$ao.Body.total }
        foreach ($it in @($ao.Body.items)) {
            if ($it -and ($allowed -notcontains [string]$it.businessUnit)) { $bad++ }
        }
    }
    Check 'assetowner1 sees only own-scope assets' $ao 200 '' ([bool]($aoTotal -gt 0 -and $aoTotal -lt $oTotal -and $bad -eq 0)) 'count not smaller than officer1 or foreign business unit leaked'

    $outId = Find-AssetId 'SVM-DB-001'
    if ($outId) {
        $r = Api 'GET' "/api/v1/assets/$outId" 'assetowner1'
        Check 'assetowner1 GET out-of-scope asset' $r 404 'ASSET_NOT_FOUND'
    }
    else {
        Skip 'assetowner1 GET out-of-scope asset' 'seed asset SVM-DB-001 not found'
    }

    $inId = Find-AssetId 'SVM-APP-001'
    if ($inId) {
        $r = Api 'GET' "/api/v1/assets/$inId" 'assetowner1'
        Check 'assetowner1 GET in-scope asset' $r 200
    }
    else {
        Skip 'assetowner1 GET in-scope asset' 'seed asset SVM-APP-001 not found'
    }

    $body = @{ assetCode = ('SMK-' + $script:Ctx.Ts); name = ('Smoke asset ' + $script:Ctx.Ts); assetType = 'Server'; environment = 'Dev' }
    $r = Api 'POST' '/api/v1/assets' 'assetowner1' $body
    Check 'assetowner1 POST asset (no asset.write)' $r 403
}

function Test-Intake {
    $ts = $script:Ctx.Ts

    $r = Api 'GET' '/api/v1/findings/import-template' 'officer1'
    Check 'officer1 GET import-template' $r 200

    $csv = "assetCode,sourceSystem,sourceRecordId,findingName,severity,cveId,location,port,component`r`n" +
    "SVM-SRV-001,SmokeTest,SMK-${ts}-1,Smoke import Log4Shell ${ts},Critical,CVE-2021-44228,https://smoke-imp-${ts}.svm.example/imp,8443,smoke-imp-${ts}`r`n" +
    "SVM-SRV-001,SmokeTest,SMK-${ts}-2,Smoke import bad row ${ts},,CVE-2016-2183,https://smoke-bad-${ts}.svm.example/bad,8444,smoke-bad-${ts}`r`n"
    $csvBytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($csv)
    $fileName = "smoke-import-${ts}.csv"
    $tx = "smoke-${ts}"

    $mp = New-Multipart $fileName $csvBytes @{ transactionId = $tx } 'text/csv'
    $r = Api 'POST' '/api/v1/findings/import' 'officer1' $null $mp
    Check 'officer1 POST import (1 good + 1 bad row)' $r 200 '' ([bool]($r.Body -and $r.Body.status -eq 'Partial' -and [int]$r.Body.failedCount -eq 1 -and $r.Body.replayed -eq $false)) 'expected Partial with 1 failed row'
    $hit = $false
    if ($r.Body) {
        foreach ($e in @($r.Body.errors)) {
            if ($e -and [int]$e.row -eq 2 -and ([string]$e.reason) -match 'severity') { $hit = $true }
        }
    }
    Check 'import lists the bad row (row 2, severity)' $r 200 '' $hit 'error row 2 / severity missing'

    $mp = New-Multipart $fileName $csvBytes @{ transactionId = $tx } 'text/csv'
    $r = Api 'POST' '/api/v1/findings/import' 'officer1' $null $mp
    Check 'import same transactionId -> replayed' $r 200 '' ([bool]($r.Body -and $r.Body.replayed -eq $true)) 'replayed flag not true'

    $jsonBytes = [System.Text.Encoding]::UTF8.GetBytes('[{"assetCode":"SVM-SRV-001","sourceSystem":"SmokeTest","findingName":"x","severity":"High"}]')
    $mp = New-Multipart 'smoke-json.csv' $jsonBytes @{} 'text/csv'
    $r = Api 'POST' '/api/v1/findings/import' 'officer1' $null $mp
    Check 'import JSON content in .csv -> 415' $r 415 'UNSUPPORTED_FORMAT'

    $xlsxBytes = [byte[]](0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00)
    $mp = New-Multipart 'smoke.xlsx' $xlsxBytes @{} 'application/octet-stream'
    $r = Api 'POST' '/api/v1/findings/import' 'officer1' $null $mp
    Check 'import .xlsx -> 415' $r 415 'UNSUPPORTED_FORMAT'

    # scenario A: the connected happy path instance (Critical-risk path, CVE not re-detected by the mock scanner)
    $bodyA = Get-ManualBody 'A' 'SVM-SRV-001' 'CVE-2014-0160' 'Critical' 8443
    $r = Api 'POST' '/api/v1/findings/manual' 'analyst1' $bodyA
    $idA = ''
    if ($r.Body -and $r.Body.instanceId) { $idA = [string]$r.Body.instanceId }
    Check 'analyst1 POST findings/manual (scenario A)' $r 201 '' ([bool]($idA -and $r.Body.outcome -eq 'Created')) 'outcome not Created / no instanceId'
    if ($r.Status -eq 201 -and $idA -and $r.Body.outcome -eq 'Created') {
        $script:Ctx.A = @{ Id = $idA; Rv = ''; Level = ''; TaskId = ''; Stage = 'New' }
    }

    $r2 = Api 'POST' '/api/v1/findings/manual' 'analyst1' $bodyA
    Check 'repeat same manual body -> Duplicate' $r2 201 '' ([bool]($r2.Body -and $r2.Body.outcome -eq 'Duplicate' -and $r2.Body.isDuplicate -eq $true -and (-not $idA -or [string]$r2.Body.instanceId -eq $idA))) 'not a duplicate of the same instance'

    # scenario O: instance on a Corporate Finance asset = out of scope for assetowner1
    $bodyO = Get-ManualBody 'O' 'SVM-DB-001' 'CVE-2016-2183' 'Medium' 8446
    $r = Api 'POST' '/api/v1/findings/manual' 'officer1' $bodyO
    $idO = ''
    if ($r.Body -and $r.Body.instanceId) { $idO = [string]$r.Body.instanceId }
    Check 'officer1 POST findings/manual (finance asset, scenario O)' $r 201 '' ([bool]($idO -and $r.Body.outcome -eq 'Created')) 'outcome not Created / no instanceId'
    if ($r.Status -eq 201 -and $idO) { $script:Ctx.O = @{ Id = $idO } }

    $r = Api 'GET' '/api/v1/dedup-queue' 'auditor1'
    Check 'auditor1 GET dedup-queue' $r 403
    $r = Api 'GET' '/api/v1/dedup-queue' 'officer1'
    Check 'officer1 GET dedup-queue' $r 200
}

function Test-Risk {
    $a = $script:Ctx.A
    if (-not $a) { Skip 'risk checks' 'scenario A instance was not created (intake)'; return }
    $id = $a.Id

    $r = Api 'POST' "/api/v1/instances/$id/recalculate" 'analyst1' @{}
    $inRange = $false
    if ($r.Body -and $null -ne $r.Body.score) {
        $sc = [double]$r.Body.score
        $inRange = ($sc -ge 0 -and $sc -le 100)
    }
    Check 'analyst1 POST recalculate' $r 200 '' $inRange 'score outside 0..100'
    if (-not (Update-Rv $a $r)) { Skip 'remaining risk checks' 'no rowVersion from recalculate'; return }
    $rv0 = $a.Rv

    $r = Api 'PUT' "/api/v1/instances/$id/risk" 'analyst1' @{ riskLevel = 'Critical'; rowVersion = $rv0 }
    Check 'PUT risk without reason -> 400' $r 400

    $r = Api 'PUT' "/api/v1/instances/$id/risk" 'analyst1' @{ riskLevel = 'Critical'; reason = 'Smoke test: pin Critical to exercise independent verification'; rowVersion = $rv0 }
    Check 'PUT risk with reason' $r 200 '' ([bool]($r.Body -and $r.Body.riskLevel -eq 'Critical')) 'riskLevel not Critical'
    if (Update-Rv $a $r) { $a.Level = [string]$r.Body.riskLevel }

    $r = Api 'PUT' "/api/v1/instances/$id/risk" 'analyst1' @{ riskLevel = 'Critical'; reason = 'stale rowVersion probe'; rowVersion = $rv0 }
    Check 'PUT risk with stale rowVersion -> 409' $r 409 'CONCURRENCY_CONFLICT'

    $r = Api 'POST' "/api/v1/instances/$id/recalculate" 'approver1' @{}
    Check 'approver1 POST recalculate' $r 403
}

function Test-Workflow {
    $a = $script:Ctx.A
    $o = $script:Ctx.O
    if (-not $a -or -not $a.Rv) { Skip 'workflow checks' 'scenario A instance / rowVersion unavailable (intake/risk)'; return }
    $id = $a.Id

    $h = Api 'GET' "/api/v1/instances/$id/history" 'officer1'
    Check 'officer1 GET history' $h 200 '' ([bool]($h.Body -and $h.Body.currentStatus -eq 'New')) 'current status not New'

    $rvOld = $a.Rv
    $t = Do-Transition 'officer1' $id 'UnderReview' $rvOld
    Check 'officer1 transition New->UnderReview' $t 200 '' ([bool]($t.Body -and $t.Body.toStatus -eq 'UnderReview')) 'toStatus mismatch'
    $moved = Update-Rv $a $t

    if ($moved) {
        $t = Do-Transition 'officer1' $id 'Confirmed' $rvOld
        Check 'transition with old rowVersion -> 409' $t 409 'CONCURRENCY_CONFLICT'

        $t = Do-Transition 'officer1' $id 'Resolved' $a.Rv
        Check 'illegal jump UnderReview->Resolved -> 422' $t 422 'WORKFLOW_INVALID_TRANSITION'
    }
    else {
        Skip 'stale rowVersion -> 409 / illegal jump -> 422' 'first transition failed'
    }

    $t = Do-Transition 'auditor1' $id 'Confirmed' $a.Rv
    Check 'auditor1 transition' $t 403

    $r = Api 'GET' "/api/v1/instances/$id/history" 'assetowner1'
    Check 'assetowner1 GET history in-scope instance' $r 200

    if ($o) {
        $r = Api 'GET' ("/api/v1/instances/" + $o.Id + "/history") 'assetowner1'
        Check 'assetowner1 GET history out-of-scope instance' $r 404 'INSTANCE_NOT_FOUND'
        $t = Do-Transition 'assetowner1' $o.Id 'UnderReview' 'AAAAAAAAAAA='
        Check 'assetowner1 transition out-of-scope instance' $t 404 'INSTANCE_NOT_FOUND'
    }
    else {
        Skip 'out-of-scope instance checks' 'scenario O instance was not created (intake)'
    }

    if ($moved) {
        $t = Do-Transition 'officer1' $id 'Confirmed' $a.Rv
        Check 'officer1 transition UnderReview->Confirmed' $t 200 '' ([bool]($t.Body -and $t.Body.toStatus -eq 'Confirmed')) 'toStatus mismatch'
        if (Update-Rv $a $t) { $a.Stage = 'Confirmed' }
    }
}

function Test-Assignment {
    $a = $script:Ctx.A
    if (-not $a -or -not $a.Rv -or $a.Stage -ne 'Confirmed') { Skip 'assignment / evidence / comment / notification checks' 'scenario A is not in Confirmed (workflow)'; return }
    $id = $a.Id
    $remId = (Get-Sess 'remediation1').UserId

    $r = Api 'POST' "/api/v1/instances/$id/assign" 'remediation1' @{ assignToUserId = $remId }
    Check 'remediation1 POST assign (no instance.assign)' $r 403

    $r = Api 'POST' "/api/v1/instances/$id/assign" 'officer1' @{ assignToUserId = $remId }
    Check 'officer1 POST assign to remediation1' $r 200 '' ([bool]($r.Body -and $r.Body.status -eq 'Assigned' -and [string]$r.Body.assignedToUserId -eq $remId -and $r.Body.taskId)) 'not Assigned to remediation1 / no task'
    if ($r.Status -eq 200 -and $r.Body.taskId) { $a.TaskId = [string]$r.Body.taskId }
    $assigned = Update-Rv $a $r

    if ($assigned) {
        $t = Do-Transition 'officer1' $id 'Assigned' $a.Rv
        Check 'officer1 transition Confirmed->Assigned' $t 200 '' ([bool]($t.Body -and $t.Body.toStatus -eq 'Assigned')) 'toStatus mismatch'
        if (Update-Rv $a $t) { $a.Stage = 'Assigned' }
    }
    if ($a.Stage -eq 'Assigned') {
        $t = Do-Transition 'remediation1' $id 'InProgress' $a.Rv
        Check 'remediation1 transition Assigned->InProgress' $t 200 '' ([bool]($t.Body -and $t.Body.toStatus -eq 'InProgress')) 'toStatus mismatch'
        if (Update-Rv $a $t) { $a.Stage = 'InProgress' }
    }
    else {
        Skip 'remediation1 transition Assigned->InProgress' 'assignment steps failed'
    }

    if ($a.TaskId) {
        $r = Api 'GET' ('/api/v1/tasks/' + $a.TaskId) 'remediation1'
        Check 'remediation1 GET task' $r 200 '' ([bool]($r.Body -and [string]$r.Body.ownerUserId -eq $remId)) 'task owner is not remediation1'
    }
    else {
        Skip 'remediation1 GET task' 'no task id'
    }

    # evidence: EICAR (assembled at runtime, memory only) must be rejected; clean text accepted
    $r = Send-Evidence 'remediation1' $id 'smoke-eicar.txt' (Get-EicarBytes)
    Check 'evidence upload EICAR test string -> 422' $r 422 'EVIDENCE_MALWARE'

    $r = Send-Evidence 'remediation1' $id ("smoke-evidence-" + $script:Ctx.Ts + ".txt") (Get-CleanEvidenceBytes 'A')
    Check 'evidence upload clean .txt' $r 201 '' ([bool]($r.Body -and $r.Body.scanStatus -eq 'Clean')) 'scanStatus not Clean'
    if ($r.Status -eq 201) { $a.Evidence = $true }

    $r = Api 'GET' "/api/v1/instances/$id/evidence" 'remediation1'
    $names = ''
    $n = 0
    if ($r.Status -eq 200 -and $r.Body) {
        foreach ($e in @($r.Body)) { if ($e) { $n++; $names += ([string]$e.fileName).ToLowerInvariant() + ';' } }
    }
    Check 'evidence list has the clean file only (no EICAR)' $r 200 '' ([bool]($n -ge 1 -and $names -notmatch 'eicar')) 'missing clean file or EICAR stored'

    $r = Api 'POST' "/api/v1/instances/$id/comments" 'officer1' @{ body = ('Smoke note ' + $script:Ctx.Ts + ' @remediation1 please review') }
    Check 'officer1 POST comment' $r 201 '' ([bool]($r.Body -and $r.Body.id)) 'no comment id'

    $r = Api 'POST' "/api/v1/instances/$id/comments" 'auditor1' @{ body = 'auditors cannot comment' }
    Check 'auditor1 POST comment' $r 403

    # notifications: remediation1 got the ASSIGNMENT row for scenario A; nobody else sees it
    $r = Api 'GET' '/api/v1/notifications?pageSize=100' 'remediation1'
    $notifId = ''
    if ($r.Status -eq 200 -and $r.Body) {
        foreach ($nt in @($r.Body.items)) {
            if ($nt -and [string]$nt.type -eq 'ASSIGNMENT' -and [string]$nt.relatedInstanceId -eq $id) { $notifId = [string]$nt.id }
        }
    }
    Check 'remediation1 GET notifications (own ASSIGNMENT row)' $r 200 '' ([bool]$notifId) 'ASSIGNMENT notification for scenario A not found'

    $r = Api 'GET' '/api/v1/notifications?pageSize=100' 'exec1'
    $foreign = 0
    if ($r.Status -eq 200 -and $r.Body) {
        foreach ($nt in @($r.Body.items)) {
            if ($nt -and [string]$nt.relatedInstanceId -eq $id) { $foreign++ }
        }
    }
    Check 'exec1 GET notifications (only own rows)' $r 200 '' ([bool]($foreign -eq 0)) 'sees notifications of scenario A'

    if ($notifId) {
        $r = Api 'POST' "/api/v1/notifications/$notifId/read" 'exec1'
        Check 'exec1 mark foreign notification read' $r 404 'NOTIFICATION_NOT_FOUND'
        $r = Api 'POST' "/api/v1/notifications/$notifId/read" 'remediation1'
        Check 'remediation1 mark own notification read' $r 204
    }
    else {
        Skip 'notification mark-read checks' 'no notification id'
    }
}

function Test-Sla {
    $a = $script:Ctx.A
    $o = $script:Ctx.O
    if (-not $a) { Skip 'SLA checks' 'scenario A instance was not created (intake)'; return }
    $id = $a.Id

    $r = Api 'GET' "/api/v1/instances/$id/sla" 'officer1'
    $state = ''
    $blocked = ''
    if ($r.Body) { $state = [string]$r.Body.state; $blocked = [string]$r.Body.blockedReason }
    $okState = ($state -eq 'Running') -or ($state -eq 'NotStarted' -and $blocked)
    Check 'officer1 GET sla (Running or NotStarted+blockedReason)' $r 200 '' ([bool]$okState) ('unexpected state: ' + $state)

    $r = Api 'GET' "/api/v1/instances/$id/sla" ''
    Check 'GET sla without token' $r 401

    if ($o) {
        $r = Api 'GET' ("/api/v1/instances/" + $o.Id + "/sla") 'assetowner1'
        Check 'assetowner1 GET sla out-of-scope instance' $r 404 'INSTANCE_NOT_FOUND'
    }
}

function Test-Verification {
    $a = $script:Ctx.A

    # --- scenario A (Critical): submit by officer1 -> independent verifier rule
    if ($a -and $a.Stage -eq 'InProgress' -and $a.Evidence -and $a.Rv) {
        $id = $a.Id
        $r = Api 'POST' "/api/v1/instances/$id/submit-verification" 'officer1' @{ rowVersion = $a.Rv; notes = 'Smoke: submitted for verification' }
        Check 'A: officer1 submit-verification' $r 200 '' ([bool]($r.Body -and $r.Body.toStatus -eq 'PendingVerification')) 'toStatus not PendingVerification'
        if (Update-Rv $a $r) { $a.Stage = 'Pending' }
    }
    else {
        Skip 'A: submit-verification' 'scenario A is not InProgress with clean evidence (assignment)'
    }

    if ($a -and $a.Stage -eq 'Pending') {
        $id = $a.Id
        $r = Api 'POST' "/api/v1/instances/$id/rescan" 'analyst1'
        Check 'A: analyst1 rescan (CVE-2014-0160 not re-detected)' $r 200 '' ([bool]($r.Body -and $r.Body.status -eq 'Resolved' -and $r.Body.reopened -eq $false)) 'expected status Resolved, reopened false'

        if ($a.Level -eq 'Critical') {
            $r = Api 'POST' "/api/v1/instances/$id/verification" 'officer1' @{ result = 'Verified'; method = 'Rescan'; notes = 'submitter verifying own work'; rowVersion = $a.Rv }
            Check 'A: submitter officer1 records Verified on Critical -> 422' $r 422 'INDEPENDENT_VERIFICATION_REQUIRED'
        }
        else {
            Skip 'A: submitter records Verified on Critical -> 422' 'instance is not Critical (risk override failed)'
        }

        $r = Api 'POST' "/api/v1/instances/$id/verification" 'remediation1' @{ result = 'Verified'; method = 'Manual'; rowVersion = $a.Rv }
        Check 'A: remediation1 record verification (no verification.record)' $r 403

        $r = Api 'POST' "/api/v1/instances/$id/verification" 'analyst1' @{ result = 'Verified'; method = 'Rescan'; notes = 'independent verification'; rowVersion = $a.Rv }
        Check 'A: independent analyst1 records Verified -> Resolved' $r 200 '' ([bool]($r.Body -and $r.Body.toStatus -eq 'Resolved' -and $r.Body.isIndependent -eq $true)) 'not Resolved / not independent'
        if (Update-Rv $a $r) { $a.Stage = 'Resolved' }
    }
    else {
        Skip 'A: rescan / record Verified checks' 'scenario A did not reach PendingVerification'
    }

    # --- scenario B (non-Critical, CVE not re-detected): remediation1 submits, NotResolved -> Reopened
    $b = Drive-ToInProgress 'B' 'CVE-2016-2183' 'Low' 8445
    Check-Setup 'B: setup create..InProgress' $b
    if ($b.Ok) {
        $id = $b.Id
        $r = Api 'POST' "/api/v1/instances/$id/submit-verification" 'remediation1' @{ rowVersion = $b.Rv; notes = 'no evidence yet' }
        Check 'B: submit-verification without evidence -> 422' $r 422 'MISSING_EVIDENCE'

        $r = Send-Evidence 'remediation1' $id ("smoke-evidence-B-" + $script:Ctx.Ts + ".txt") (Get-CleanEvidenceBytes 'B')
        Check 'B: remediation1 upload clean evidence' $r 201 '' ([bool]($r.Body -and $r.Body.scanStatus -eq 'Clean')) 'scanStatus not Clean'

        $r = Api 'POST' "/api/v1/instances/$id/submit-verification" 'remediation1' @{ rowVersion = $b.Rv; notes = 'fix applied' }
        Check 'B: remediation1 submit-verification' $r 200 '' ([bool]($r.Body -and $r.Body.toStatus -eq 'PendingVerification')) 'toStatus not PendingVerification'
        if (Update-Rv $b $r) {
            $r = Api 'POST' "/api/v1/instances/$id/rescan" 'analyst1'
            Check 'B: analyst1 rescan' $r 200 '' ([bool]($r.Body -and $r.Body.status -eq 'Resolved' -and $r.Body.reopened -eq $false)) 'expected status Resolved, reopened false'

            $r = Api 'POST' "/api/v1/instances/$id/verification" 'analyst1' @{ result = 'NotResolved'; method = 'Rescan'; notes = 'smoke: still broken'; rowVersion = $b.Rv }
            Check 'B: analyst1 record NotResolved -> Reopened' $r 200 '' ([bool]($r.Body -and $r.Body.toStatus -eq 'Reopened')) 'toStatus not Reopened'
            if (Update-Rv $b $r) {
                $r = Api 'POST' "/api/v1/instances/$id/verification" 'analyst1' @{ result = 'Verified'; method = 'Manual'; rowVersion = $b.Rv }
                Check 'B: record when not PendingVerification -> 422' $r 422 'VERIFICATION_NOT_PENDING'
            }
        }
        else {
            Skip 'B: rescan / record NotResolved' 'submit failed'
        }
    }
    else {
        Skip 'B: verification checks' 'setup failed'
    }

    # --- scenario C: CVE-2021-44228 is the one CVE the mock scanner still detects after a fix
    $c = Drive-ToInProgress 'C' 'CVE-2021-44228' 'Critical' 8447
    Check-Setup 'C: setup create..InProgress' $c
    if ($c.Ok) {
        $id = $c.Id
        $r = Send-Evidence 'remediation1' $id ("smoke-evidence-C-" + $script:Ctx.Ts + ".txt") (Get-CleanEvidenceBytes 'C')
        $evOk = ($r.Status -eq 201)
        $r = Api 'POST' "/api/v1/instances/$id/submit-verification" 'remediation1' @{ rowVersion = $c.Rv; notes = 'fix applied' }
        Check 'C: upload evidence + remediation1 submit-verification' $r 200 '' ([bool]($evOk -and $r.Body -and $r.Body.toStatus -eq 'PendingVerification')) 'evidence upload or submit failed'
        if (Update-Rv $c $r) {
            $r = Api 'POST' "/api/v1/instances/$id/rescan" 'analyst1'
            Check 'C: analyst1 rescan CVE-2021-44228 still detected -> Reopened' $r 200 '' ([bool]($r.Body -and $r.Body.status -eq 'StillDetected' -and $r.Body.reopened -eq $true -and $r.Body.toStatus -eq 'Reopened')) 'expected StillDetected + reopened'
        }
        else {
            Skip 'C: rescan' 'submit failed'
        }
    }
    else {
        Skip 'C: rescan checks' 'setup failed'
    }
}

function Test-Audit {
    $a = $script:Ctx.A

    $r = Api 'GET' '/api/v1/audit?pageSize=20' 'auditor1'
    Check 'auditor1 GET audit' $r 200 '' ([bool]($r.Body -and (Cnt $r.Body.items) -gt 0)) 'no audit rows'

    $r = Api 'GET' '/api/v1/audit/verify' 'auditor1'
    Check 'auditor1 GET audit/verify (hash chain)' $r 200 '' ([bool]($r.Body -and $r.Body.isValid -eq $true)) 'hash chain not valid'

    if ($a) {
        $r = Api 'GET' ('/api/v1/audit?objectType=VulnerabilityInstance&pageSize=50&objectId=' + $a.Id) 'auditor1'
        Check 'auditor1 GET audit for scenario A instance' $r 200 '' ([bool]($r.Body -and (Cnt $r.Body.items) -ge 3)) 'fewer than 3 audit rows for the instance'
    }
    else {
        Skip 'audit rows for scenario A instance' 'scenario A instance was not created (intake)'
    }

    $r = Api 'GET' '/api/v1/audit' 'officer1'
    Check 'officer1 GET audit' $r 403
    $r = Api 'GET' '/api/v1/audit/verify' 'officer1'
    Check 'officer1 GET audit/verify' $r 403
}

# ============================================================================= bootstrap helpers

function Get-DemoPassword {
    $pw = ''
    try {
        $lines = & dotnet user-secrets list --project $script:ApiProj 2>$null
        foreach ($line in @($lines)) {
            if ([string]$line -match '^\s*Seed:DemoPassword\s*=\s*(.*)$') { $pw = $Matches[1].Trim() }
        }
    }
    catch { $pw = '' }
    if (-not $pw -and $env:Seed__DemoPassword) { $pw = [string]$env:Seed__DemoPassword }
    if (-not $pw -and $Password) { $pw = $Password }
    return $pw
}

function Test-PortFree([int]$PortNo) {
    $l = $null
    try {
        $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $PortNo)
        $l.Start()
        return $true
    }
    catch { return $false }
    finally {
        if ($null -ne $l) { try { $l.Stop() } catch { } }
    }
}

# only exception type names / generic phrases from the API logs - never message text (may hold connection details)
function Get-LogHint([string[]]$Files) {
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($f in $Files) {
        if (-not $f -or -not (Test-Path -LiteralPath $f)) { continue }
        $t = ''
        try { $t = Get-Content -LiteralPath $f -Raw -ErrorAction Stop } catch { continue }
        if (-not $t) { continue }
        if ($t -match 'Failed to bind|address already in use') { if (-not $names.Contains('port bind failure')) { $names.Add('port bind failure') } }
        foreach ($m in [regex]::Matches($t, '\b[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*Exception\b')) {
            if ($names.Count -lt 3 -and -not $names.Contains($m.Value)) { $names.Add($m.Value) }
        }
    }
    return ($names -join ', ')
}

function Invoke-Module([string]$Name, [scriptblock]$Body, [bool]$Quiet) {
    $script:CurModule = $Name
    $script:Quiet = $Quiet
    try { & $Body }
    catch {
        $script:Quiet = $false
        Add-Result 'FAIL' ($Name + ' module crashed') '' '' '' ($_.Exception.GetType().Name + ' at script line ' + $_.InvocationInfo.ScriptLineNumber)
    }
    $script:Quiet = $false
}

# ============================================================================= main

$apiProc = $null
$outLog = $null
$errLog = $null
$prevEnv = $env:ASPNETCORE_ENVIRONMENT
$aborted = $false

try {
    if (-not (Test-Path -LiteralPath $script:ApiProj)) {
        Write-Host ('ABORT: API project not found at ' + $script:ApiProj) -ForegroundColor Red
        $aborted = $true
    }

    $allModules = @('health', 'auth', 'reference', 'assets', 'intake', 'risk', 'workflow', 'assignment', 'sla', 'verification', 'audit')
    $deps = @{
        risk         = @('intake')
        workflow     = @('intake', 'risk')
        assignment   = @('intake', 'risk', 'workflow')
        sla          = @('intake', 'risk')
        verification = @('intake', 'risk', 'workflow', 'assignment')
    }
    $selected = @($allModules)
    if ($Only -notcontains 'all') { $selected = @($Only) }
    $needed = New-Object System.Collections.Generic.List[string]
    foreach ($m in $selected) {
        if (-not $needed.Contains($m)) { $needed.Add($m) }
        if ($deps.ContainsKey($m)) { foreach ($d in $deps[$m]) { if (-not $needed.Contains($d)) { $needed.Add($d) } } }
    }
    $needLogin = $false
    foreach ($m in $needed) { if ($m -ne 'health') { $needLogin = $true } }

    if (-not $aborted -and $needLogin) {
        $script:Pw = Get-DemoPassword
        if (-not $script:Pw) {
            Write-Host 'ABORT: demo password not found (user-secrets Seed:DemoPassword, env Seed__DemoPassword or -Password).' -ForegroundColor Red
            $aborted = $true
        }
    }

    # --- start the API (unless -BaseUrl points at a running one)
    if (-not $aborted) {
        if ($BaseUrl) {
            $script:Base = $BaseUrl.TrimEnd('/')
            Write-Host ('Testing already running API at ' + $script:Base) -ForegroundColor Cyan
        }
        else {
            $chosen = 0
            for ($i = 0; $i -lt 25; $i++) {
                if (Test-PortFree ($Port + $i)) { $chosen = $Port + $i; break }
            }
            if ($chosen -eq 0) {
                Write-Host ('ABORT: no free local port in ' + $Port + '..' + ($Port + 24) + '. Use -BaseUrl or -Port.') -ForegroundColor Red
                $aborted = $true
            }
            else {
                if ($chosen -ne $Port) { Write-Host ('Port ' + $Port + ' is busy, using ' + $chosen) -ForegroundColor Yellow }
                $tmp = [System.IO.Path]::GetTempPath()
                $stamp = [guid]::NewGuid().ToString('N')
                $outLog = Join-Path $tmp ('svm-smoke-out-' + $stamp + '.log')
                $errLog = Join-Path $tmp ('svm-smoke-err-' + $stamp + '.log')
                $env:ASPNETCORE_ENVIRONMENT = 'Development'
                $dotnetArgs = @('run', '--no-build', '--no-launch-profile', '--project', ('"' + $script:ApiProj + '"'), '--urls', ('http://localhost:' + $chosen))
                $apiProc = Start-Process -FilePath 'dotnet' -ArgumentList $dotnetArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput $outLog -RedirectStandardError $errLog
                $script:Base = 'http://127.0.0.1:' + $chosen
                Write-Host ('Starting API on port ' + $chosen + ' (waiting up to 60 s for /health) ...') -ForegroundColor Cyan

                $healthy = $false
                $deadline = (Get-Date).AddSeconds(60)
                while ((Get-Date) -lt $deadline) {
                    if ($apiProc.HasExited) { break }
                    $h = Send-Http 'GET' '/health' $null @{} $null '' 3
                    if ($h.Status -eq 200) { $healthy = $true; break }
                    Start-Sleep -Milliseconds 1000
                }
                if (-not $healthy) {
                    $hint = Get-LogHint @($outLog, $errLog)
                    $exitText = ''
                    if ($apiProc.HasExited) { $exitText = ' (process exited with code ' + $apiProc.ExitCode + ')' }
                    Write-Host ('ABORT: API did not become healthy within 60 s' + $exitText + '.') -ForegroundColor Red
                    if ($hint) { Write-Host ('  log hints (type names only): ' + $hint) -ForegroundColor Red }
                    Write-Host '  check: SQL Server up, migrations applied, dotnet build done, user-secrets Auth:Jwt:SigningKey / ConnectionStrings:Svm set.' -ForegroundColor Red
                    $script:CurModule = 'startup'
                    Add-Result 'FAIL' 'API start / GET /health' '200' '0' '' 'API did not become healthy'
                    $aborted = $true
                }
            }
        }
    }

    # --- run the modules
    if (-not $aborted) {
        $map = @{
            health       = { Test-Health }
            auth         = { Test-Auth }
            reference    = { Test-Reference }
            assets       = { Test-Assets }
            intake       = { Test-Intake }
            risk         = { Test-Risk }
            workflow     = { Test-Workflow }
            assignment   = { Test-Assignment }
            sla          = { Test-Sla }
            verification = { Test-Verification }
            audit        = { Test-Audit }
        }
        foreach ($m in $allModules) {
            if (-not $needed.Contains($m)) { continue }
            $isQuiet = ($selected -notcontains $m)
            Invoke-Module $m $map[$m] $isQuiet
        }
    }
}
finally {
    if ($null -ne $apiProc) {
        try { & taskkill.exe /PID $apiProc.Id /T /F 2>&1 | Out-Null } catch { }
        Start-Sleep -Milliseconds 700
    }
    foreach ($f in @($outLog, $errLog)) {
        if ($f) {
            for ($i = 0; $i -lt 5; $i++) {
                if (-not (Test-Path -LiteralPath $f)) { break }
                try { Remove-Item -LiteralPath $f -Force -ErrorAction Stop } catch { Start-Sleep -Milliseconds 400 }
            }
        }
    }
    $env:ASPNETCORE_ENVIRONMENT = $prevEnv
}

# ============================================================================= summary

$pass = @($script:Results | Where-Object { $_.Status -eq 'PASS' }).Count
$fail = @($script:Results | Where-Object { $_.Status -eq 'FAIL' }).Count
$skip = @($script:Results | Where-Object { $_.Status -eq 'SKIP' }).Count
$total = $script:Results.Count

Write-Host ''
$summaryColor = 'Green'
if ($fail -gt 0 -or $aborted) { $summaryColor = 'Red' }
Write-Host ('TOTAL {0}  PASS {1}  FAIL {2}  SKIP {3}' -f $total, $pass, $fail, $skip) -ForegroundColor $summaryColor
foreach ($f in @($script:Results | Where-Object { $_.Status -eq 'FAIL' })) {
    $line = 'FAIL  [{0}] {1}  expected={2} actual={3}' -f $f.Module, $f.Name, $f.Exp, $f.Act
    if ($f.Code) { $line += '  code=' + $f.Code }
    if ($f.Why) { $line += '  (' + $f.Why + ')' }
    Write-Host $line -ForegroundColor Red
}
if ($aborted -and $fail -eq 0) { Write-Host 'Run aborted before any check executed (see ABORT message above).' -ForegroundColor Red }

if ($fail -eq 0 -and -not $aborted) { exit 0 } else { exit 1 }
