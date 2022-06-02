param([string] $outputPath, [string] $options)

$daasDllPath = [io.path]::combine($PSScriptRoot, "DAAS.dll")
Add-Type -Path $daasDllPath

[DaaS.Logger]::Init("",$outputPath, "jMapCollector", $true) 

[DaaS.Logger]::LogDiagnoserVerboseEvent("Checking running java.exe process")

$javaProcesses = Get-Process | Where { $_.Name -eq "java" or $_.Name -eq "javaw" } | select -expand id
$jmapProcess = [io.path]::combine($env:JAVA_HOME, 'bin', 'jmap.exe')
$jmapExists = Test-Path $jmapProcess
$machineName = hostname

#find out the path of the rt.jar from java.exe's File Handles
foreach ($javaProcess in $javaProcesses)
{
	$rtJarFile = kuduhandles $javaProcess | where { $_.EndsWith('\jre\lib\rt.jar') } | Select -First 1
	[DaaS.Logger]::LogDiagnoserVerboseEvent("rt.jar path is [$rtJarFile]")
    if ($rtJarFile.length -gt 0)
    {
		$parentpath = $rtJarFile.ToLower().Replace("\jre\lib\rt.jar","").Replace("c:","d:")
        if (test-path $parentpath)
        {
			$jmapProcess = [io.path]::combine($parentpath, 'bin', 'jmap.exe')
			[DaaS.Logger]::LogDiagnoserVerboseEvent("jMap Process path is [$jmapProcess]")
			if (test-path $jmapProcess)
			{
				$jmapExists = $true
				break
			}
		}
		else
		{
			[DaaS.Logger]::LogDiagnoserVerboseEvent("testpath returned false, parentPath is [$parentpath]")
		}
	}
}

if ($jmapExists -eq $true)
{
    foreach ($javaProcess in $javaProcesses)
    {
		$outFilePath = [io.path]::combine($outputPath, $machineName + "_" + $javaProcess.ToString())

		if ($options.Contains("text"))
		{
			$outFile =  $outFilePath+ "_MemorySummary.txt"
			$cmdToExecute = "Executing: " + $jmapProcess + " -heap " + $javaProcess + " > " +  $outFile 
            [DaaS.Logger]::LogDiagnoserVerboseEvent($cmdToExecute)
			&$jmapProcess -heap $javaProcess > $outFile
			[DaaS.Logger]::LogDiagnoserVerboseEvent("jMap collected -heap output for the process $javaProcess")

			$outFile =  $outFilePath+ "_ObjectHistogram.txt"
			$cmdToExecute = "Executing: " + $jmapProcess + "-F -histo " + $javaProcess + " > " +  $outFile 
            [DaaS.Logger]::LogDiagnoserVerboseEvent($cmdToExecute)
			&$jmapProcess -F -histo $javaProcess > $outFile
			[DaaS.Logger]::LogDiagnoserVerboseEvent("jMap collected -histo output for the process $javaProcess")
		}
		else
		{
			$outFile =  $outFilePath+ "_MemoryDump.bin"

			if ($jmapProcess.ToLower().Contains("program files (x86)"))
			{
				$cmdToExecute = "Executing: " + $jmapProcess + "-F -J-d32 -dump:format=b,file=" + $outFile + " " + $javaProcess
                [DaaS.Logger]::LogDiagnoserVerboseEvent($cmdToExecute)
				&$jmapProcess -F -J-d32 -dump:live,format=b,file=$outfile $javaProcess
				[DaaS.Logger]::LogDiagnoserVerboseEvent("jMap finished collecting binary output" )
			}
			else
			{
				$cmdToExecute = "Executing: " + $jmapProcess + "-F -J-d64 -dump:format=b,file=" + $outFile + " " + $javaProcess
                [DaaS.Logger]::LogDiagnoserVerboseEvent($cmdToExecute)
				&$jmapProcess -F -J-d64 -dump:live,format=b,file=$outfile $javaProcess
				[DaaS.Logger]::LogDiagnoserVerboseEvent("jMap finished collecting binary output" )
			}
		}
    
    }

    [DaaS.Logger]::LogDiagnoserVerboseEvent("jMap Collector finished collecting data")
}
