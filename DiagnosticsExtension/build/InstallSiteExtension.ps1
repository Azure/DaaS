#------- Sample script to install DaaS site extension on multiple sites at once------
#------------------------------------------------------------------------------------
# The script reads json from sites.json file and install DaaS site extension on all of 
# them at once

Class Credentials
{
    [string] $kuduEndpoint
    [string] $resourceGroupName
    [string] $authorizationHeader

}

Function GetResourceGroupForWebApp ([string] $webappNameParam)
{
    $script:ResourceGroupName = ""
    Get-AzureRmResource | where { ($_.ResourceType -match "Microsoft.Web/sites") -and ($_.ResourceId.ToLower().EndsWith($webappNameParam.ToLower()))} | foreach {        
       $script:ResourceGroupName= $_.ResourceGroupName
       return
    }
    return $script:ResourceGroupName
}

Function GetPublishingCredsForWebApp([string] $subscriptionId, [string] $webAppName)
{
    $script:creds = New-Object Credentials
    
    Select-AzureRmSubscription -SubscriptionId $subscriptionId  | Out-Null
    $rgName = GetResourceGroupForWebApp $webAppName

    $publishProfileString = Invoke-AzureRmResourceAction -ResourceGroupName $rgName `
     -ResourceType Microsoft.Web/sites `
     -ResourceName $webAppName `
     -Action publishxml `
     -ApiVersion 2015-08-01 -Force

    $publishProfileXml =[xml]$publishProfileString

    $userName = $publishProfileXml.publishData.publishProfile[0].userName
    $password = $publishProfileXml.publishData.publishProfile[0].userPWD

    $script:creds.kuduEndpoint = $publishProfileXml.publishData.publishProfile[0].publishUrl
    $script:creds.authorizationHeader = "Basic {0}" -f [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(("{0}:{1}" -f  $userName, $password)))
    $script:creds.resourceGroupName = $rgName

    $script:creds.kuduEndpoint

    return $script:creds
}


$ProgressPreference = 'SilentlyContinue'
$sitesFile = $args[0]
"Using file $sitesFile"
$mode = $args[1]

$allWebSites = (Get-Content $sitesFile | Out-String | ConvertFrom-Json)

if ($mode -ne "uninstall"){
    $siteExtensionArchive = "C:\temp\DaasSiteExtension.zip"
    if (Test-Path $siteExtensionArchive) {
          "Removing existing temporary zip file"
          Remove-Item $siteExtensionArchive
          "Removed temporary file"
        }
}


#.\build.bat
#.\publish.bat

if ([string]::IsNullOrEmpty($(Get-AzureRmContext).Account)) {Login-AzureRmAccount}

foreach($webapp in $allWebSites)
{
    $webAppName = $webapp.SiteName
    $subscriptionId  = $webapp.SubscriptionId

    $creds = GetPublishingCredsForWebApp $subscriptionId $webAppName 
    $kuduEndpoint = $creds.kuduEndpoint
    $authorizationHeader = $creds.authorizationHeader
    $rgName = $creds.resourceGroupName

    "[$webAppName]"
    $kuduApiUrl = "https://$kuduEndpoint/api/zip/SiteExtensions/DaaS/"

    "    Deleting exsiting site extension if any"

    try{
        Invoke-RestMethod -Uri "https://$kuduEndpoint/api/vfs/SiteExtensions/DaaS/?recursive=true" `
                            -Headers @{"Authorization"=$authorizationHeader;"If-Match"="*"} `
                            -Method DELETE | Out-Null
    }
    catch {
        Write-Host "    Encountered " + $_.Exception.Response.StatusCode + " while deleting DaaS site extension"
    }

    "    Stopping WebApp" 
    Invoke-AzureRmResourceAction –ResourceGroupName $rgName -ResourceType Microsoft.Web/sites -ResourceName $webAppName -Action stop -ApiVersion 2015-08-01 -Force

    "    Starting WebApp"
    Invoke-AzureRmResourceAction -ResourceGroupName $rgName -ResourceType Microsoft.Web/sites -ResourceName $webAppName -Action start -ApiVersion 2015-08-01 -Force
}

if ($mode -ne "uninstall"){
    Compress-Archive -Path  "C:\source\DaaS\DiagnosticsExtension\bin\Release\PublishOutput\*" -DestinationPath $siteExtensionArchive -Force
    Timeout /t 20 /nobreak
}

foreach($webapp in $allWebSites)
{

    $webAppName = $webapp.SiteName
    $subscriptionId  = $webapp.SubscriptionId
    
    $script:creds = GetPublishingCredsForWebApp $subscriptionId $webAppName 
    $kuduEndpoint = $script:creds.kuduEndpoint
    $authorizationHeader = $script:creds.authorizationHeader
    $rgName = $script:creds.resourceGroupName
    
    $kuduApiUrl = "https://$kuduEndpoint/api/zip/SiteExtensions/DaaS/"
    
    "[$webAppName]"
    $DaaSVersion = Invoke-RestMethod -Uri "https://$kuduEndpoint/daas/api/v2/daasversion" `
                        -Headers @{"Authorization"=$authorizationHeader;"If-Match"="*"}

    "    Existing DaaS version is " + $DaaSVersion.Version

    
    if ($mode -ne "uninstall") {

        "    Uploading Site Extension"
    
        try{
            Invoke-RestMethod -Uri $kuduApiUrl `
                                -Headers @{"Authorization"=$authorizationHeader;"If-Match"="*"} `
                                -Method PUT `
                                -InFile $siteExtensionArchive `
                                -ContentType "multipart/form-data" | Out-Null
        }
        catch {
            Write-Host "    Encountered " + $_.Exception.Response.StatusCode + " while uploading DaaS site extension"
        }

        "    Site Extension uploaded"

        # Action Stop
        "    Stopping WebApp" 
        Invoke-AzureRmResourceAction –ResourceGroupName $rgName -ResourceType Microsoft.Web/sites -ResourceName $webAppName -Action stop -ApiVersion 2015-08-01 -Force

        # Action stop
        "    Starting WebApp"
        Invoke-AzureRmResourceAction -ResourceGroupName $rgName -ResourceType Microsoft.Web/sites -ResourceName $webAppName -Action start -ApiVersion 2015-08-01 -Force
    }

}


if ($mode -ne "uninstall") {
    #Give some time for the site to warmup
    Timeout /t 20 /nobreak

    foreach($webapp in $allWebSites)
    {
        $webAppName = $webapp.SiteName
        $subscriptionId  = $webapp.SubscriptionId
        
        $script:creds = GetPublishingCredsForWebApp $subscriptionId $webAppName 
        $kuduEndpoint = $script:creds.kuduEndpoint
        $authorizationHeader = $script:creds.authorizationHeader
        $rgName = $script:creds.resourceGroupName
        
        "[$webAppName]"
        "    DaaS Warming up"
    
        $DaaSVersion = Invoke-RestMethod -Uri "https://$kuduEndpoint/daas/api/v2/daasversion" `
                                -Headers @{"Authorization"=$authorizationHeader;"If-Match"="*"}

        "    DaaS version is " + $DaaSVersion.Version
    }
}