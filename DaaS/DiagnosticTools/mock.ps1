param([string] $logfile, [string] $outputPath)
$fileName = [System.IO.Path]::GetFileNameWithoutExtension($logfile) + "_Analyzer"
$fileContents = "Mock Analyzer started at " + [System.DateTime]::Now.ToString() + "`n"
start-sleep -Seconds 70
$fileContents += "Mock Analyzer Finished at " + [System.DateTime]::Now.ToString() + "`n"
$outFile = [io.path]::Combine($outputPath , $fileName)
$fileContents | Out-File -FilePath $outFile 