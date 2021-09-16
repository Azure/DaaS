param([string] $outputPath)

Add-Type -Path "DAAS.dll"
[DaaS.Logger]::Init("", $outputPath, "mock", $true) 
[DaaS.Logger]::LogDiagnoserVerboseEvent("Running Mock collector")


$fileName = "Collector_" + [System.DateTime]::Now.Ticks.ToString() + ".txt"
$fileContents = "Mock Collector started at " + [System.DateTime]::Now.ToString() + "`n"

[DaaS.Logger]::LogStatus("Mock Collector started")

start-sleep -Seconds 10

[DaaS.Logger]::LogStatus("Mock Collector finished")

$fileContents += "Mock Collector Finished at " + [System.DateTime]::Now.ToString() + "`n"
$outFile = [io.path]::Combine($outputPath , $fileName)
$fileContents | Out-File -FilePath $outFile
