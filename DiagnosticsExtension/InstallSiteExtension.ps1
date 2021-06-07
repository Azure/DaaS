#------- Sample script to install DaaS site extension on multiple sites at once------
#------------------------------------------------------------------------------------
# The script reads json from sites.json file and install DaaS site extension on all of 
# them at once

$ProgressPreference = 'SilentlyContinue'

$allWebSites = (Get-Content '.\sites.json' | Out-String | ConvertFrom-Json)

$siteExtensionArchive = "C:\temp\DaasSiteExtension.zip"
if (Test-Path $siteExtensionArchive) {
      "Removing existing temporary zip file"
      Remove-Item $siteExtensionArchive
      "Removed temporary file"
    }

foreach($webapp in $allWebSites)
{
    $webAppName = $webapp.SiteName
    $userName = '$' + $webapp.SiteName
    $userPassword = $webapp.PublishingPassword
    $subscriptionId  = $webapp.SubscriptionId
    $rgName = $webapp.ResourceGroup
    $kuduEndpoint = $webapp.KuduEndpoint
    $authorizationHeader = "Basic {0}" -f [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(("{0}:{1}" -f $userName , $userPassword)))

    "[$webAppName]"
    Select-AzureRmSubscription -SubscriptionId $subscriptionId  | Out-Null
 
    
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

Compress-Archive -Path  "C:\source\DaaS\DiagnosticsExtension\bin\Release\PublishOutput\*" -DestinationPath $siteExtensionArchive -Force

foreach($webapp in $allWebSites)
{

    $webAppName = $webapp.SiteName
    $userName = '$' + $webapp.SiteName
    $userPassword = $webapp.PublishingPassword
    $subscriptionId  = $webapp.SubscriptionId
    $rgName = $webapp.ResourceGroup
    $kuduEndpoint = $webapp.KuduEndpoint
    $kuduApiUrl = "https://$kuduEndpoint/api/zip/SiteExtensions/DaaS/"
    $authorizationHeader = "Basic {0}" -f [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(("{0}:{1}" -f $userName , $userPassword)))
    
    "[$webAppName]"
    $DaaSVersion = Invoke-RestMethod -Uri "https://$webAppName.scm.azurewebsites.net/daas/api/v2/daasversion" `
                        -Headers @{"Authorization"=$authorizationHeader;"If-Match"="*"}

    "    Existing DaaS version is " + $DaaSVersion.Version

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


#Give some time for the site to warmup
Timeout /t 20 /nobreak

foreach($webapp in $allWebSites)
{
    $webAppName = $webapp.SiteName
    $userName = '$' + $webapp.SiteName
    $userPassword = $webapp.PublishingPassword
    $kuduEndpoint = $webapp.KuduEndpoint
    $kuduApiUrl = "https://$kuduEndpoint/api/zip/SiteExtensions/DaaS/"
    $authorizationHeader = "Basic {0}" -f [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(("{0}:{1}" -f $userName , $userPassword)))
    
    "[$webAppName]"
    "    DaaS Warming up"
    
    $DaaSVersion = Invoke-RestMethod -Uri "https://$kuduEndpoint/daas/api/v2/daasversion" `
                            -Headers @{"Authorization"=$authorizationHeader;"If-Match"="*"}

    "    DaaS version is " + $DaaSVersion.Version
}