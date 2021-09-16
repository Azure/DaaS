param([string] $dumpFile, [string] $outputPath)

$programfiles = [System.Environment]::ExpandEnvironmentVariables("%ProgramFiles(x86)%")
[string] $dumpAnalyzerpath = [System.Io.Path]::Combine($programfiles, "DumpAnalyzer")
[string] $daasSymbolPath = [System.Environment]::ExpandEnvironmentVariables("%HOME%\Data\DaaS\symbols")

[string] $symbolpath = "$Env:SystemDrive\NdpCorePdb;srv*$daasSymbolPath*http://msdl.microsoft.com/download/symbols;%ProgramFiles(x86)%\PHP\v5.6\Debug;%ProgramFiles(x86)%\PHP\v5.5\Debug;%ProgramFiles(x86)%\PHP\v5.4\Debug;%ProgramFiles(x86)%\PHP\v5.3\Debug\" 
[string] $rules = "CrashHangAnalysis"
[string] $tempDir = $Env:TEMP

Add-Type -Path "DAAS.dll"
[DaaS.Logger]::Init("",$outputPath, "DumpAnalyzer", $false) 

$dumpfileSize =  0
$dumpFileExists = Test-Path $dumpFile
if ($dumpFileExists)
{
	$dumpfileSize = (Get-Item $dumpFile).length
	if ($dumpfileSize -gt 800mb)
	{
		[DaaS.Logger]::LogDiagnoserVerboseEvent("DumpFile size is greater than 800MB so running both CrashHangAnalysis and DotNetMemoryAnalysis")
		$rules = $rules + ",DotNetMemoryAnalysis"
	}
	[DaaS.Logger]::LogDiagnoserVerboseEvent("Going to analyze $dumpFile of size $dumpfileSize with rules $rules") } else {
	[DaaS.Logger]::LogDiagnoserVerboseEvent("$dumpFile does not exist") }

$pathExists = Test-Path "$Env:TEMP\DumpAnalyzer\DumpAnalyzer.exe" 
if ($pathExists -eq $false)
{
    $dumpAnalyzerTempPath = [System.Io.Path]::Combine($tempDir, "DumpAnalyzer")
    Remove-Item -path $dumpAnalyzerTempPath -recurse 
    "Copying $dumpAnalyzerpath to $tempDir"
    Copy-Item -Path $dumpAnalyzerpath  -Recurse -Destination $tempDir -Container }

$dumpAnalyzerTempPath = "$Env:TEMP\DumpAnalyzer\DumpAnalyzer.exe"
$cmdToExecute = "Executing: " + $dumpAnalyzerTempPath + " -dumpFile $dumpFile -symbols $symbolpath -Rules $rules -out $outputPath"
[DaaS.Logger]::LogDiagnoserVerboseEvent($cmdToExecute)

&$dumpAnalyzerTempPath -dumpFile "$dumpFile" -symbols "$symbolpath" -Rules $rules -out "$outputPath"
[DaaS.Logger]::LogDiagnoserVerboseEvent("DumpAnalyzer completed")

$diagnosticsAnalysisLauncher = [IO.Path]::Combine($PSScriptRoot, 'DiagnosticAnalysisLauncher.exe')
[DaaS.Logger]::LogDiagnoserVerboseEvent("DiagnosticAnalysisLauncher path = " + $diagnosticsAnalysisLauncher)
&$diagnosticsAnalysisLauncher "$dumpFile"

