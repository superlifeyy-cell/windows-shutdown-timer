param([string]$OutputDirectory='', [switch]$Verify)
$ErrorActionPreference='Stop'
$timerRoot=$PSScriptRoot
$timerOutput=if($OutputDirectory) {[IO.Path]::GetFullPath($OutputDirectory)} else {Join-Path $timerRoot 'dist'}
New-Item -ItemType Directory -Path $timerOutput -Force | Out-Null
$timerCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path $timerCompiler)) {$timerCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
if(-not (Test-Path $timerCompiler)) {throw 'The .NET Framework C# compiler was not found.'}
$timerSource=@('native_model.cs','native_platform.cs','native_controls.cs','native_app.cs') | ForEach-Object {Join-Path $timerRoot ('src\'+$_)}
$timerExe=Join-Path $timerOutput 'ShutdownTimer.exe'
$timerArguments=@('/nologo','/target:winexe','/platform:anycpu','/optimize+','/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',('/out:'+$timerExe),('/win32manifest:'+(Join-Path $timerRoot 'src\shutdown_timer.manifest')),('/win32icon:'+(Join-Path $timerRoot 'assets\shutdown_timer.ico')),('/resource:'+(Join-Path $timerRoot 'assets\shutdown_timer.ico')+',ShutdownTimer.icon'))
& $timerCompiler @timerArguments @timerSource
if($LASTEXITCODE -ne 0) {throw 'Compilation failed.'}
if($Verify) {
    $timerTests=Join-Path $timerOutput 'NativeTests.exe'
    $timerTestArgs=@('/nologo','/target:exe','/main:ShutdownTimer.NativeTests','/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',('/out:'+$timerTests),('/resource:'+(Join-Path $timerRoot 'assets\shutdown_timer.ico')+',ShutdownTimer.icon'))
    & $timerCompiler @timerTestArgs @timerSource (Join-Path $timerRoot 'tests\native_tests.cs')
    if($LASTEXITCODE -ne 0) {throw 'Test compilation failed.'}
    & $timerTests
    if($LASTEXITCODE -ne 0) {throw 'Native tests failed.'}
    $timerPreview=Join-Path $timerOutput 'previews'
    $timerProcess=Start-Process -FilePath $timerExe -ArgumentList @('--preview',('"'+$timerPreview+'"')) -PassThru
    if(-not $timerProcess.WaitForExit(60000)) {$timerProcess.Kill(); throw 'Preview generation timed out.'}
    if($timerProcess.ExitCode -ne 0) {throw 'Preview generation failed.'}
}
Copy-Item (Join-Path $timerRoot 'docs\release-v2.1.11.txt') (Join-Path $timerOutput '使用说明.txt') -Force
$timerZip=Join-Path $timerOutput 'ShutdownTimer-v2.1.11.zip'
$timerChecksum=(Get-FileHash $timerExe -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $timerOutput 'SHA256SUMS.txt'),($timerChecksum+'  ShutdownTimer.exe'+"`r`n"),[Text.UTF8Encoding]::new($false))
Compress-Archive -LiteralPath $timerExe,(Join-Path $timerOutput '使用说明.txt'),(Join-Path $timerOutput 'SHA256SUMS.txt') -DestinationPath $timerZip -Force
$timerZipHash=(Get-FileHash $timerZip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::AppendAllText((Join-Path $timerOutput 'SHA256SUMS.txt'),($timerZipHash+'  ShutdownTimer-v2.1.11.zip'+"`r`n"),[Text.UTF8Encoding]::new($false))
Write-Output ('Built native desktop application: '+$timerExe)
