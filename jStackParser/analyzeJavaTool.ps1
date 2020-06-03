param([string] $logfile, [string] $outputPath, [string] $htmlFilePath)

function GetUrlForFile([string] $filepath)
	{
    $siteName = [Environment]::GetEnvironmentVariable("WEBSITE_HOSTNAME")  
    
    if ([string]::IsNullOrWhiteSpace($siteName))
    {
		return ""
	}

    if ($siteName.EndsWith(".p.azurewebsites.net"))
		{
			#This site is on ASE
            $siteName = $siteName.Replace(".p.azurewebsites.net", "")
            #Now we have sitename.asename
            $siteNameArr = $sitename.Split('.')
            if ($siteNameArr.Length -eq 2)
			{
				$siteName = $siteNameArr[0] + ".scm." + $siteNameArr[1] + ".p.azurewebsites.net"
			}
			else
            {
				return ""
			}
		}
	else
		{
			$siteName = $siteName.Replace(".azurewebsites.net", ".scm.azurewebsites.net")
		}

	$url = $filepath.ToLower().Replace("d:\local\temp\logs\", "https://$siteName/api/vfs/Data/Daas/Logs/")
    $url = $url.Replace('\', '/')
	return $url
	}

$fileContents = ""
$fileName = ""

if ($logfile.EndsWith(".txt") -eq $true)
{
	$fileContents = Get-Content -Path $logfile -Raw
	$fileName = [System.IO.Path]::GetFileNameWithoutExtension($logfile) + "-" + $(((get-date).ToUniversalTime()).ToString("yyyyMMddThhmmssZ")) + ".txt"
}
else
{
	$url = GetUrlForFile $logfile
	$htmlfile = join-path $psscriptroot $htmlFilePath
	$fileContents = Get-Content -Path $htmlfile
	$fileContents = $fileContents.Replace('{url}',$url)
	$fileContents = $fileContents.Replace('{logfile}',$logfile)
	$fileName = [System.IO.Path]::GetFileNameWithoutExtension($logfile) + "-" + $(((get-date).ToUniversalTime()).ToString("yyyyMMddThhmmssZ")) + ".html"
}

$outFile = [io.path]::Combine($outputPath , $fileName)
$fileContents | Out-File -FilePath $outFile 