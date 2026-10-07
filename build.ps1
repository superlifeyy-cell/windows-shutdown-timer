param([string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$timerRoot=$PSScriptRoot
$timerRuntime=Join-Path $timerRoot 'build\runtime'
$timerOutput=if($OutputDirectory) {[IO.Path]::GetFullPath($OutputDirectory)} else {Join-Path $timerRoot 'dist'}
New-Item -ItemType Directory -Path $timerRuntime,$timerOutput -Force | Out-Null
function Convert-TimerScriptAscii([string]$Text) {
    $timerBuffer=[Text.StringBuilder]::new()
    foreach($timerCharacter in $Text.ToCharArray()) {
        if([int]$timerCharacter -lt 128) {[void]$timerBuffer.Append($timerCharacter)}
        else {[void]$timerBuffer.Append(('\u{0:x4}' -f [int]$timerCharacter))}
    }
    return $timerBuffer.ToString()
}
function Convert-TimerHtmlAscii([string]$Text) {
    $timerBuffer=[Text.StringBuilder]::new()
    foreach($timerCharacter in $Text.ToCharArray()) {
        if([int]$timerCharacter -lt 128) {[void]$timerBuffer.Append($timerCharacter)}
        else {[void]$timerBuffer.Append(('&#{0};' -f [int]$timerCharacter))}
    }
    return $timerBuffer.ToString()
}
function Read-TimerSource([string]$Name) {
    return [IO.File]::ReadAllText((Join-Path $timerRoot ('src\'+$Name)),[Text.Encoding]::UTF8).Replace("`r`n","`n")
}
$timerHtml=Read-TimerSource 'shutdown_timer.template.htm'
$timerUi=Read-TimerSource 'shutdown_timer_ui.template.js'
$timerInline=[regex]::new('<script language="javascript">.*?</script>',[Text.RegularExpressions.RegexOptions]::Singleline)
$timerHtml=$timerInline.Replace($timerHtml,[Text.RegularExpressions.MatchEvaluator]{param($Match) '<script language="javascript" src="shutdown_timer_common.js"></script>'+"`n"+'<script language="javascript">'+"`n"+$timerUi+"`n"+'</script>'},1)
$timerScripts=[regex]::new('<script\b.*?</script>',[Text.RegularExpressions.RegexOptions]::Singleline -bor [Text.RegularExpressions.RegexOptions]::IgnoreCase)
$timerHtml=$timerScripts.Replace($timerHtml,[Text.RegularExpressions.MatchEvaluator]{param($Match) Convert-TimerScriptAscii $Match.Value})
$timerHtml=Convert-TimerHtmlAscii $timerHtml
$timerCommon=Convert-TimerScriptAscii (Read-TimerSource 'shutdown_timer_common.template.js')
[IO.File]::WriteAllText((Join-Path $timerRuntime 'shutdown_timer.hta'),$timerHtml.Replace("`n","`r`n"),[Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $timerRuntime 'shutdown_timer_common.js'),$timerCommon.Replace("`n","`r`n"),[Text.Encoding]::ASCII)
Copy-Item -LiteralPath (Join-Path $timerRoot 'assets\shutdown_timer.ico') -Destination (Join-Path $timerRuntime 'shutdown_timer.ico') -Force
$timerCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path -LiteralPath $timerCompiler)) {$timerCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
if(-not (Test-Path -LiteralPath $timerCompiler)) {throw 'The .NET Framework 4.x C# compiler was not found.'}
$timerExe=Join-Path $timerOutput '定时关机.exe'
$timerArguments=@('/nologo','/target:winexe','/platform:anycpu','/optimize+',('/out:'+$timerExe),('/win32manifest:'+(Join-Path $timerRoot 'src\shutdown_timer.manifest')),('/win32icon:'+(Join-Path $timerRuntime 'shutdown_timer.ico')))
foreach($timerAsset in @('shutdown_timer.hta','shutdown_timer_common.js','shutdown_timer.ico')) {
    $timerArguments+=('/resource:'+(Join-Path $timerRuntime $timerAsset)+',ShutdownTimer.'+$timerAsset)
}
$timerArguments+=(Join-Path $timerRoot 'src\shutdown_timer_tray.cs')
$timerArguments+=(Join-Path $timerRoot 'src\shutdown_timer_bundle.cs')
& $timerCompiler @timerArguments
if($LASTEXITCODE -ne 0) {throw 'Compilation failed.'}
$timerZip=Join-Path $timerOutput '定时关机_单文件版.zip'
Compress-Archive -LiteralPath $timerExe -DestinationPath $timerZip -Force
$timerChecksum=(Get-FileHash -LiteralPath $timerExe -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $timerOutput 'SHA256SUMS.txt'),($timerChecksum+'  定时关机.exe'+"`r`n"),[Text.UTF8Encoding]::new($false))
Write-Output ('Built: '+$timerExe)
