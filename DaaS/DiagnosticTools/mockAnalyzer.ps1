param([string] $logfile, [string] $outputPath)

Add-Type -Path "DAAS.dll"
[DaaS.Logger]::Init("",$outputPath, "mock", $false) 
[DaaS.Logger]::LogDiagnoserVerboseEvent("Running Mock collector")

$fileName = [System.IO.Path]::GetFileNameWithoutExtension($logfile) + "_Analyzer.txt"
$fileContents = "Mock Analyzer started analyzing file " + $logFile +  "at " + [System.DateTime]::Now.ToString() + "`n"

[DaaS.Logger]::LogStatus("Mock analyzer started")

start-sleep -Seconds 10

[DaaS.Logger]::LogStatus("Mock analyzer finished")

$fileContents += "Mock Analyzer Finished at " + [System.DateTime]::Now.ToString() + "`n"
$outFile = [io.path]::Combine($outputPath , $fileName)
$fileContents | Out-File -FilePath $outFile 
