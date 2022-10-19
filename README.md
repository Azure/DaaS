[![Build Status](https://msazure.visualstudio.com/Antares/_apis/build/status/Azure.DaaS?branchName=main)](https://msazure.visualstudio.com/Antares/_build/latest?definitionId=291420&branchName=main)

# Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

# Running DaaS as a Private Site Extension
You can build this project locally and install it as a Private Site extension on your Azure App by following these steps.
1. In Visual Studio, make sure you set the nuget package source to: https://api.nuget.org/v3/index.json
1. Increment the `AssemblyFileVersion` and `AssemblyVersion` inside `DaaS\DaaS\Properties\AssemblyInfo.cs` and `DaaS\DaaSRunner\Properties\AssemblyInfo.cs`. They should be higher than this repository's version and they should be exactly the same in both these files.
1. Make sure that the Release configuration from Visual Studio is chosen. Then, rebuild the whole project and right click on the **DiagnosticsExtension** project and choose **Publish** and Publish the **FolderProfile**.
1. Take the published content and drag and drop it to **d:\home\SiteExtensions\DAAS** folder via Kudu Console. (you may have to create the folder first).
1. Restart the site via Azure Portal to ensure that the app points to DAAS private extension bits.
1. Now DAAS Extension bits should be pointing to Private Site Extensions folder (**d:\home\SiteExtensions\DAAS**). To validate, you can browse to **https://&lt;yoursitename&gt;.scm.azurewebsites.net/DaaS/api/v2/daasversion** and you should see the version that you specified in Step 1. 
