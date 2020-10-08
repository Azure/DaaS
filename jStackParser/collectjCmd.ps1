param([string] $outputPath)

Add-Type -Path "DAAS.dll"
[DaaS.Logger]::Init("",$outputPath, "jCmdCollector", $true) 

[DaaS.Logger]::LogDiagnoserVerboseEvent("Checking running java.exe process")

$javaProcesses = Get-Process -Name java |select -expand id
$jcmdProcess = [io.path]::combine($env:JAVA_HOME, 'bin', 'jcmd.exe')
$jcmdExists = Test-Path $jcmdProcess
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
			$jcmdProcess = [io.path]::combine($parentpath, 'bin', 'jcmd.exe')
			[DaaS.Logger]::LogDiagnoserVerboseEvent("jcmd Process path is [$jcmdProcess]")
			if (test-path $jcmdProcess)
			{
				$jcmdExists = $true
				break
			}
		}
	}
}

if ($jcmdExists -eq $true)
{
    foreach ($javaProcess in $javaProcesses)
    {
		$jfrFileName = $machineName + "_" + $javaProcess.ToString() + "_jcmd.jfr"

		## For some reason jCMD.exe is not able to write files to %SystemDrive%\local\temp directory
		## so we generate a file in %SystemDrive%\home\logfiles\ticks folder and move it to TEMP to keep DAAS 
		## happy. Later we delete this folder.

		$dirPath = [io.path]::combine("$Env:HOME\logfiles","JFR",(get-date).ticks)
		$dirExists = test-path $dirPath
		if ($dirExists -eq $false)
		{
		    [io.directory]::createdirectory($dirPath)    
		}
		$dirExists = test-path $outputPath
		if ($dirExists -eq $false)
		{
			[io.directory]::createdirectory($outputPath)    
		}

	   $jfrFilePath = [io.path]::combine($dirPath, $jfrFileName)
       $outFile = [io.path]::combine($outputPath, $jfrFileName)
       $cmdToExecute = "Executing: $jcmdProcess $javaProcess JFR.start  name=AppServiceJavaFlightRecorder settings=profile duration=60s filename=$jfrFilePath"
	   $cmdToExecute
		[DaaS.Logger]::LogDiagnoserVerboseEvent($cmdToExecute)
       $jcmdOutput = &$jcmdProcess $javaProcess JFR.start  name=AppServiceJavaFlightRecorder settings=profile duration=60s filename=$jfrFilePath
	   $jcmdOutput	   
		
	   if ($jcmdOutput -clike "*Started recording*")
	   {
		   [DaaS.Logger]::LogDiagnoserVerboseEvent("jcmd started for process $javaProcess, sleeping for 65 seconds for Recording to finish, jcmd Output = $jcmdOutput")
		   "jcmd started for process $javaProcess, sleeping for 65 seconds for Recording to finish"
	       [DaaS.Logger]::LogStatus("Java Flight Recorder started, will stop automatically after 60 seconds")
	       
		   Start-Sleep -Seconds 65
		   $recordingRunning = &$jcmdProcess $javaProcess JFR.check
		   $count = 0
		   $recordingRunning 
		   while ($recordingRunning.Contains("AppServiceJavaFlightRecorder") -and $count -lt 5 -and ($recordingRunning.Contains("(stopped)") -eq $false))
		   {
				Start-Sleep -Seconds 5
				$recordingRunning = &$jcmdProcess $javaProcess JFR.check
				[DaaS.Logger]::LogDiagnoserVerboseEvent("Waiting for jcmd Recording to finish for $javaProcess JFR.Check output = $recordingRunning")
				"Waiting for jcmd Recording to finish for $javaProcess JFR.Check output = $recordingRunning"
				$count++
		   }
		  
		  [DaaS.Logger]::LogStatus("Java Flight Recorder finished recording, moving files to correct folders")
		  "moving file from $jfrFilePath to $outFile"
		   [DaaS.Logger]::LogDiagnoserVerboseEvent( "moving file from $jfrFilePath to $outFile")
		   if (test-path $outFile)
		   {
		   	   del $outFile
		   }
		   move-item -path $jfrFilePath $outFile
		}

        [DaaS.Logger]::LogStatus("Java Flight Recorder finished, cleaning up temporary files")
		if (test-path $dirPath)
		{
			[io.directory]::delete($dirPath, $true)
			[DaaS.Logger]::LogDiagnoserVerboseEvent( "deleted $dirPath")
			"deleted $dirPath"
		}
    
    }

	[DaaS.Logger]::LogDiagnoserVerboseEvent("jcmd Collected completed")
}
else{
	[DaaS.Logger]::LogDiagnoserVerboseEvent("Could not find jcmd Process path")
}
