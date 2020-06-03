param([string] $outputPath)

Add-Type -Path "DAAS.dll"
[DaaS.Logger]::Init("",$outputPath, "jStackCollector", $true) 

[DaaS.Logger]::LogDiagnoserVerboseEvent("Checking running java.exe process")

$javaProcesses = Get-Process -Name java |select -expand id
$jstackProcess = [io.path]::combine($env:JAVA_HOME, 'bin', 'jstack.exe')
$jstackExists = Test-Path $jstackProcess
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
			$jstackProcess = [io.path]::combine($parentpath, 'bin', 'jstack.exe')
			[DaaS.Logger]::LogDiagnoserVerboseEvent("jStack Process path is [$jstackProcess]")
			if (test-path $jstackProcess)
			{
				$jstackExists = $true
				break
			}
		}
		else
		{
			[DaaS.Logger]::LogDiagnoserVerboseEvent("testpath returned false, parentPath is [$parentpath]")
		}
	}
}

if ($jstackExists -eq $true)
{
    foreach ($javaProcess in $javaProcesses)
    {
       $outFile = [io.path]::combine($outputPath, $machineName + "_" + $javaProcess.ToString() + "_jstack.log")
       $cmdToExecute = "Executing: " + $jstackProcess + " -F " + $javaProcess + ">" + $outFile 
		[DaaS.Logger]::LogDiagnoserVerboseEvent($cmdToExecute)
       &$jstackProcess -F $javaProcess > $outFile
	   [DaaS.Logger]::LogDiagnoserVerboseEvent("jStack collected stacks for the process $javaProcess")
    
    }

	[DaaS.Logger]::LogDiagnoserVerboseEvent("jStack Collected completed")
}
else{
	[DaaS.Logger]::LogDiagnoserVerboseEvent("Could not find jstack Process path")
}
