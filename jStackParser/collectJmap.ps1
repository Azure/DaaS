param([string] $outputPath, [string] $options)

$daasDllPath = [io.path]::combine($PSScriptRoot, "DAAS.dll")
Add-Type -Path $daasDllPath

[DaaS.Logger]::Init("",$outputPath, "jMapCollector", $true) 

[DaaS.Logger]::LogDiagnoserVerboseEvent("Checking running java.exe process")

$javaProcesses = Get-Process | Where { $_.Name -eq "java" -or $_.Name -eq "javaw" } | select -expand id
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
        $jmapFileName = $machineName + "_" + $javaProcess.ToString() + "_MemoryDump.bin"

        ## For some reason jMap.exe is not able to write files to %SystemDrive%\local\temp directory
        ## so we generate a file in %SystemDrive%\home\logfiles\ticks folder and move it to TEMP to keep DAAS 
        ## happy. Later we delete this folder.

        $dirPath = [io.path]::combine("$Env:HOME\logfiles","JMAP_DAAS",(get-date).ticks)
        $jMapPath = [io.path]::combine("$Env:HOME\logfiles","JMAP_DAAS")
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

        $jmapFilePath = [io.path]::combine($dirPath, $jmapFileName)
        $outFile = [io.path]::combine($outputPath, $jmapFileName)
        
        $cmdToExecute = "Executing: " + $jmapProcess + "-dump:format=b,file=" + $jmapFilePath + " " + $javaProcess
        [DaaS.Logger]::LogDiagnoserVerboseEvent($cmdToExecute)
        &$jmapProcess -dump:format=b,file=$jmapFilePath $javaProcess
        [DaaS.Logger]::LogDiagnoserVerboseEvent("jMap finished collecting binary output" )
        
        [DaaS.Logger]::LogStatus("jMap finished collecting memory dumps, moving files to correct folders")
        "moving file from $jmapFilePath to $outFile"
        [DaaS.Logger]::LogDiagnoserVerboseEvent( "moving file from $jmapFilePath to $outFile")
        if (test-path $outFile)
        {
            del $outFile
        }
        move-item -path $jmapFilePath $outFile

        if (test-path $jMapPath)
        {
            Remove-Item -Recurse -Force $jMapPath
        }
    }

    [DaaS.Logger]::LogDiagnoserVerboseEvent("jMap Collector finished collecting data")
}
