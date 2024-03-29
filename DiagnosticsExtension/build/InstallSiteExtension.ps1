#------- Sample script to install DaaS site extension on multiple sites at once------
#------------------------------------------------------------------------------------
# The script reads json from sites.json file and install DaaS site extension on all of 
# them at once

Class Credentials
{
    [string] $kuduEndpoint
    [string] $resourceGroupName

}

Function GetResourceGroupForWebApp ([string] $webappNameParam)
{
    $script:ResourceGroupName = ""
    Get-AzResource -Name $webappNameParam.ToLower() -ResourceType "Microsoft.Web/sites" | foreach {
       $script:ResourceGroupName= $_.ResourceGroupName
       return
    }
    return $script:ResourceGroupName
}

Function GetPublishingCredsForWebApp([string] $subscriptionId, [string] $webAppName)
{
    $script:creds = New-Object Credentials
    
    Select-AzSubscription -SubscriptionId $subscriptionId | Out-Null
    $rgName = GetResourceGroupForWebApp $webAppName

    $publishProfileString = Invoke-AzResourceAction -ResourceGroupName $rgName `
     -ResourceType Microsoft.Web/sites `
     -ResourceName $webAppName `
     -Action publishxml `
     -ApiVersion 2015-08-01 -Force

    $publishProfileXml =[xml]$publishProfileString

    #
    # The Publish Profile Credentials below are not really used.
    # Will clean this code at some point.
    #
    
    #$userName = $publishProfileXml.publishData.publishProfile[0].userName
    #$password = $publishProfileXml.publishData.publishProfile[0].userPWD

    $script:creds.kuduEndpoint = $publishProfileXml.publishData.publishProfile[0].publishUrl
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

$diagnosticsExtensionFolder = Split-Path -Path $PSScriptRoot -Parent
$publishFolder = [IO.Path]::Combine($diagnosticsExtensionFolder,"bin", "Release", "PublishOutput")
$publishFolder = $publishFolder + "\*"
$removeFolder1 = [IO.Path]::Combine($publishFolder, "bin" ,"DiagnosticTools", "DiagnosticAnalysis_OneCore_x64")
$removeFolder2 = [IO.Path]::Combine($publishFolder, "bin", "DiagnosticTools", "DiagnosticAnalysis", "DiagnosticAnalysis_OneCore_x64")

$context = Get-AzContext
if (!$context)
{
    Connect-AzAccount
}
else
{
    Write-Host "Already connected with Az-Account"
}

if ([string]::IsNullOrEmpty($(Get-AzContext).Account)) {Login-AzAccount}
$accessToken = Get-AzAccessToken
$authorizationHeader = "Bearer " + $accessToken.Token

foreach($webapp in $allWebSites)
{
    $webAppName = $webapp.SiteName
    $subscriptionId  = $webapp.SubscriptionId

    $creds = GetPublishingCredsForWebApp $subscriptionId $webAppName 
    $kuduEndpoint = $creds.kuduEndpoint
    $rgName = $creds.resourceGroupName

    "[$webAppName]"
    $kuduApiUrl = "https://$kuduEndpoint/api/zip/SiteExtensions/DaaS/"

    "    Deleting exsiting site extension if any"
    $shouldRestartWebApp = $true

    try{
        Invoke-RestMethod -Uri "https://$kuduEndpoint/api/vfs/SiteExtensions/DaaS/?recursive=true" `
                            -Headers @{"Authorization"=$authorizationHeader;"If-Match"="*"} `
                            -Method DELETE | Out-Null
    }
    catch {
        Write-Host "    Encountered " + $_.Exception.Response.StatusCode + " while deleting DaaS site extension"
        if ($_.Exception.Response.StatusCode.ToString() -eq "NotFound" ) {
            # Site extension does not exist. No need to restart the web app
            $shouldRestartWebApp = $false
        }
    }
    
    if ($shouldRestartWebApp) {
        "    Stopping WebApp"
        Invoke-AzResourceAction -ResourceGroupName $rgName -ResourceType Microsoft.Web/sites -ResourceName $webAppName -Action stop -ApiVersion 2015-08-01 -Force
        
        "    Starting WebApp"
        Invoke-AzResourceAction -ResourceGroupName $rgName -ResourceType Microsoft.Web/sites -ResourceName $webAppName -Action start -ApiVersion 2015-08-01 -Force
    }
}

if ($mode -ne "uninstall"){
    if (Test-Path $removeFolder1) {
      Remove-Item -Recurse -Force $removeFolder1
    }
    
    if (Test-Path $removeFolder2) {
      Remove-Item -Recurse -Force $removeFolder2
    }
    
    Compress-Archive -Path  $publishFolder -DestinationPath $siteExtensionArchive -Force -CompressionLevel Fastest
    Timeout /t 20 /nobreak
}

foreach($webapp in $allWebSites)
{
    $webAppName = $webapp.SiteName
    $subscriptionId  = $webapp.SubscriptionId
    
    $script:creds = GetPublishingCredsForWebApp $subscriptionId $webAppName 
    $kuduEndpoint = $script:creds.kuduEndpoint
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
        Invoke-AzResourceAction -ResourceGroupName $rgName -ResourceType Microsoft.Web/sites -ResourceName $webAppName -Action stop -ApiVersion 2015-08-01 -Force

        # Action stop
        "    Starting WebApp"
        Invoke-AzResourceAction -ResourceGroupName $rgName -ResourceType Microsoft.Web/sites -ResourceName $webAppName -Action start -ApiVersion 2015-08-01 -Force
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
        $rgName = $script:creds.resourceGroupName
        
        "[$webAppName]"
        "    DaaS Warming up"
    
        $DaaSVersion = Invoke-RestMethod -Uri "https://$kuduEndpoint/daas/api/v2/daasversion" `
                                -Headers @{"Authorization"=$authorizationHeader;"If-Match"="*"}

        "    DaaS version is " + $DaaSVersion.Version
    }
}