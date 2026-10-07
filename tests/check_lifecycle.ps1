param([string]$TimerExecutable='', [string]$TimerPrefix='lifecycle')
$ErrorActionPreference='Stop'
$timerRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$timerTestExe=if($TimerExecutable) {(Resolve-Path -LiteralPath $TimerExecutable).Path} else {Join-Path $timerRoot 'dist/定时关机.exe'}
function Write-DemoPlan($timerStatePath) {
    $timerNow=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    [IO.File]::WriteAllText($timerStatePath,"version=1`r`ntoken=$timerNow-88`r`nstatus=scheduled`r`nstarted=$timerNow`r`ndeadline=$($timerNow+1800000)`r`nboot=`r`nheartbeat=0`r`nwarn5=0`r`nwarnFinal=0`r`nreason=`r`n",[Text.Encoding]::ASCII)
}
function Start-Demo($timerStatePath,$timerAnswer,$timerMode) {
    Start-Process -FilePath $timerTestExe -ArgumentList @('--demo','--state',('"'+$timerStatePath+'"'),'--app',('"'+(Join-Path $PSScriptRoot 'nonexistent-test-ui.hta')+'"'),'--title','"ShutdownTimer Lifecycle QA"','--demo-exit-answer',$timerAnswer,$timerMode) -WindowStyle Hidden -PassThru
}
$timerChecks=0
$timerNoState=Join-Path $PSScriptRoot ($TimerPrefix+'-no.ini')
Write-DemoPlan $timerNoState
$timerNo=Start-Demo $timerNoState 'no' '--ensure'
try {
    Start-Sleep -Milliseconds 600
    $timerSignal=Start-Demo $timerNoState 'no' '--exit'
    $timerSignal.WaitForExit(5000) | Out-Null
    Start-Sleep -Milliseconds 700
    $timerNo.Refresh()
    $timerNoText=[IO.File]::ReadAllText($timerNoState)
    $timerNoLog=[IO.File]::ReadAllText($timerNoState+'.qa-log')
    if($timerNo.HasExited -or $timerNoText -notmatch 'status=scheduled' -or $timerNoLog -notmatch 'exit=declined' -or $timerNoLog -match '(?m)^-a\r?$') {throw 'Declining exit did not preserve the running plan.'}
    $timerChecks++
    Start-Sleep -Milliseconds 600
    if([IO.File]::ReadAllText($timerNoState) -eq $timerNoText) {throw 'Tray countdown stopped after declining exit.'}
    $timerChecks++
} finally {
    $timerNo.Refresh()
    if(-not $timerNo.HasExited) {
        if($timerNo.Path -ne $timerTestExe) {throw 'Unexpected QA process path.'}
        Stop-Process -Id $timerNo.Id
    }
}
$timerYesState=Join-Path $PSScriptRoot ($TimerPrefix+'-yes.ini')
Write-DemoPlan $timerYesState
$timerYes=Start-Demo $timerYesState 'yes' '--ensure'
try {
    Start-Sleep -Milliseconds 600
    $timerSignal=Start-Demo $timerYesState 'yes' '--exit'
    $timerSignal.WaitForExit(5000) | Out-Null
    if(-not $timerYes.WaitForExit(5000)) {throw 'Accepting exit left the tray process running.'}
    $timerYesText=[IO.File]::ReadAllText($timerYesState)
    $timerYesLog=[IO.File]::ReadAllText($timerYesState+'.qa-log')
    if($timerYesText -notmatch 'status=cancelled' -or $timerYesLog -notmatch 'exit-confirm' -or $timerYesLog -notmatch '(?m)^-a\r?$') {throw 'Accepting exit did not cancel the simulated plan.'}
    $timerChecks++
} finally {
    $timerYes.Refresh()
    if(-not $timerYes.HasExited -and $timerYes.Path -eq $timerTestExe) {Stop-Process -Id $timerYes.Id}
}
$timerIdleState=Join-Path $PSScriptRoot ($TimerPrefix+'-idle.ini')
[IO.File]::WriteAllText($timerIdleState,"",[Text.Encoding]::ASCII)
$timerIdle=Start-Demo $timerIdleState 'no' '--exit'
try {
    if(-not $timerIdle.WaitForExit(5000)) {throw 'Idle exit unexpectedly required confirmation.'}
    $timerIdleLog=[IO.File]::ReadAllText($timerIdleState+'.qa-log')
    if($timerIdleLog -match 'exit-confirm' -or $timerIdleLog -match '(?m)^-a\r?$') {throw 'Idle exit prompted or issued cancellation.'}
    $timerChecks++
} finally {
    $timerIdle.Refresh()
    if(-not $timerIdle.HasExited -and $timerIdle.Path -eq $timerTestExe) {Stop-Process -Id $timerIdle.Id}
}
$timerResult="$timerChecks native process lifecycle checks passed; all Windows shutdown commands were mocked."
[IO.File]::WriteAllText((Join-Path $PSScriptRoot ($TimerPrefix+'-checks.txt')),$timerResult)
Write-Output $timerResult
