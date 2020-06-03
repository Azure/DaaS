# Version 2.0 -  April 04/07/2014
param(
	[Parameter(Mandatory=$true, HelpMessage = "[Required] File names to be processed")] 
	[string]$PathToFile='',	# example 0634d1e8-da89-46f6-a36f-879c82a2913f

	[Parameter(Mandatory = $true, HelpMessage = "[Required] Output HTML file path")]
	[string] $ReportFilePath = ""
)

function Get-ScriptDirectory
{	
	$Invocation = (Get-Variable MyInvocation -Scope 1).Value
	Split-Path $Invocation.MyCommand.Path
}

$Script:ScriptPath = Get-ScriptDirectory

$ReportDirectory = [System.IO.Path]::GetDirectoryName($ReportFilePath)


#
#In case that the passed in path to files contain wildcard, we need to get the first real file name to use as the base
#to generate other file names
#
$FileName = Get-ChildItem $PathToFile
$FirstPathToFile = $FileName[0].FullName

$FileGUID = [System.IO.Path]::GetFileNameWithoutExtension($FirstPathToFile)

$TempDirectory = [System.IO.Path]::GetTempPath()
$LogDirectory = [System.IO.Path]::GetTempPath()


$outhtmlfile = $ReportFilePath
$outcsvfile =  Join-Path $LogDirectory ("Query-" + [System.IO.Path]::GetFileNameWithoutExtension($FirstPathToFile)  + ".out")
$logFileName = "$LogDirectory\IISProcessing_" + [System.IO.Path]::GetFileNameWithoutExtension($FirstPathToFile) + ".log"

$dt = Get-Date
$piecharticon = " <img style=`"width: 12px;height: 12px`" alt=`"chart-icon`" src=`"data:image/jpeg;base64,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
`"> "
$tableicon=" <img style=`"width: 12px;height: 12px`" alt=`"table-icon`" src=`"data:image/jpeg;base64,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
`">"
$htmlOpen = "<HTML><HEAD><style>
BODY{background-color:white;font-family: Verdana,Tahoma;font-size: 10pt}
TABLE{border-width: 2px;border-style: solid;border-color: black;border-collapse: collapse;font-family: Verdana,'Lucida Console',Consolas,Tahoma;font-size: 9pt}
TH{border-width: 2px;padding: 5px;border-style: solid;border-color: black;background-color:lightblue}
TD{border-width: 1px;padding-left: 6px;padding-right: 3px;border-style: solid;border-color: black;background-color: White;padding-bottom: 4px;padding-top: 4px;text-align: left}
IMG{border-style: none}
</style><TITLE>" + $PathToFile + "</TITLE>
          <link href='script/CoreCss_555C9E47.css' rel='stylesheet' type='text/css' />
          <link href='script/ExtensionsCss_4C90087C.css' rel='stylesheet' type='text/css' />
          <link href='script/LiveSiteAssist.css' rel='stylesheet' type='text/css' />
          <link href='script/Site.css' rel='stylesheet' type='text/css' />
</HEAD><BODY>
<a id=`"reportTop`"><h1>IIS Log Analysis Report</h1></a>
<h4>Generated on file " + $PathToFile + " (" + $dt + ") </h4> " + " </br>
<hr>
<ul style=`"list-style-type: none`">
<li><a href=`"#query3`">" + $tableicon + "Request Type Distribution</a></br></li>
<li><a href=`"#query18`">" + $tableicon + "Top 20 Longest Processing Requests</a></br></li>
<li><a href=`"#query4`">" + $tableicon + "Top 20 Hits</a></br></li>
<li><a href=`"#query6`">" + $tableicon + "Top 20 ASPX Hits</a></br></li>
<li><a href=`"#query7`">" + $tableicon + "Top 20 Slowest ASPX Pages</a></br></li>
<li><a href=`"#query9`">" + $tableicon + "Top 20 Client IP Addresses</a></br></li>
<li><a href=`"#query10a`">" + $tableicon + "Requests Per Hour Table</a></br></li>
<li><a href=`"#query11`">" + $tableicon + "HTTP Status Counts</a></br></li>
<li><a href=`"#query13`">" + $tableicon + "Top 20 HTTP 304 Errors</a></br></li>
<li><a href=`"#query14`">" + $tableicon + "Top 20 HTTP 404 Errors</a></br></li>
<li><a href=`"#query15`">" + $tableicon + "Top 20 HTTP 403 Errors</a></br></li>
<li><a href=`"#query16`">" + $tableicon + "Top 20 HTTP 500 Errors</a></br></li>
<li><a href=`"#query17`">" + $tableicon + "Top 20 HTTP 503 Errors</a></br></li>
<li><a href=`"#query19`">" + $tableicon + "Top 100 Longest Processing Requests from PingDOM User Agent</a></br></li>
</ul></P>
<hr></P>"
$toReportTop = "<a href=`"#reportTop`">To report top</a><br>"
$htmlClose = "</BODY></HTML>"


function Log
{
param($logmessage)
#don't log anything for WAWS VM
#$now = [System.DateTime]::Now.ToString()
#$logFileName = "$LogDirectory\IISProcessing_" + [System.IO.Path]::GetFileNameWithoutExtension($FirstPathToFile) + ".log"
#$now + " : " + $logmessage | Out-File $logFileName -Append
}

function ExecuteQuery ($queryStr) {

 	$LogParser = Join-Path $Script:ScriptPath "LogParser.exe"

  #for test
  $ErrorActionPreference = "SilentlyContinue"
	
  try
  {
 
  	 Log ("Start LogParser: " + $queryStr)
	 $LogParserProcess = Start-Process -FilePath $LogParser -ArgumentList $queryStr -PassThru -WindowStyle Hidden  
     Log ("returned from Start.")

     $LogParserProcess.waitForExit() 
  }
  Catch
  {
      Log ("Exception on wait on running LogParser " )
  }
  Log ("LogParser Exited:  " + $LogParserProcess.HasExited)
}

function RunQuery ($query,$title, $queryout, $fWriteOutput = $null)
{	

	if ($queryout -ne $null) 
    {
        if (Test-Path $queryout)
        {
		    Remove-Item $queryout
        }
	}

    ExecuteQuery -queryStr $query


    if ( $fWriteOutput -ne $null )
    {
        return
    }
	
    $title = $title + "<p><code style=`"background-color: #f0f0f0;font-size: 11px`">" + $query + "</code></p>"
	
	if ( Test-Path $queryout )
	{
	    if ($query -like "*-o:CSV*") 
		{
            Import-Csv $queryout | ConvertTo-Html -Fragment -PreContent ($toReportTop + $title) -PostContent "</P>" | Out-File -FilePath $outhtmlfile  -Append
		}
#		elseif ($title -like "*CHART*")
#		{
#
#			 base64 encode the jpg and inline it in the html report.
#            $imgBytes = Get-Content -Path $queryout -Encoding Byte -ReadCount 0
#            $endcodedImg = [System.Convert]::ToBase64String($imgBytes)
#            $b = $b + "<P><img src=`"data:image/jpeg;base64,"+ $endcodedImg + "`" alt=`"CHART`"></P>"
#            Out-File -FilePath $outhtmlfile -Append -InputObject ($toReportTop + $title + $b)
#   		}
	}
	else
	{
		$b = "<P>No data available</P>"

        Out-File -FilePath $outhtmlfile  -Append -InputObject ($toReportTop + $title + $b)

	}
}



#
# Allows us to have spaces in the path.
#

$LPPathToFile = "'" + $PathToFile + "'"

$uriHourIpStatusStatsFile = ( $outcsvfile + "uri-hour-ip-status-stats.csv" )
$uriHourUriStatsFile = ( $outcsvfile + "uri-hour-stats.csv" )
$statsFile = ( $outcsvfile + "uri-stats.csv" )


if (Test-Path $outhtmlfile)
{
	Remove-Item $outhtmlfile
}

Out-File -FilePath $outhtmlfile -Append -InputObject $htmlOpen


if ($statsFile -ne $null) 
{
    if (Test-Path $statsFile)
    {
        Remove-Item $statsFile
    }
}

if ($uriHourIpStatusStatsFile -ne $null) 
{
    if (Test-Path $uriHourIpStatusStatsFile)
    {
        Remove-Item $uriHourIpStatusStatsFile
    }
}

if ($uriHourUriStatsFile -ne $null) 
{
    if (Test-Path $uriHourUriStatsFile)
    {
        Remove-Item $uriHourUriStatsFile
    }
}

# we use IISW3C so that even if the log doesn't include some of the fields
# we want we don't get an error.

$firstQuery = 
"`"SELECT QUANTIZE(TO_TIMESTAMP(date, time),3600) AS Hour," +
" TO_LOWERCASE( cs-uri-stem ) as cs-uri-stem," +
" sc-status," +
" sc-substatus," +
" sc-win32-status," +
" c-ip," +
" COUNT( cs-uri-stem ) as cs-uri-hits," +
" MIN( time-taken ) as time-taken-min," +
" MAX( time-taken ) as time-taken-max," +
" AVG( time-taken ) as time-taken-avg," +
" MIN( sc-bytes ) as sc-bytes-min," +
" MAX( sc-bytes ) as sc-bytes-max," +
" AVG( sc-bytes ) as sc-bytes-avg," +
" SUM( sc-bytes ) as sc-bytes-sum," +
" MIN( cs-bytes ) as cs-bytes-min," +
" MAX( cs-bytes ) as cs-bytes-max," +
" AVG( cs-bytes ) as cs-bytes-avg," +
" SUM( cs-bytes ) as cs-bytes-sum" +
" INTO " + $uriHourIpStatusStatsFile + 
" FROM " + $LPPathToFile + 
" GROUP BY Hour," +
" cs-uri-stem," +
" sc-status," +
" sc-substatus," +
" sc-win32-status," +
" c-ip" +
" ORDER BY Hour ASC`"" +
" -i: IISW3C -o: CSV"


$secondQuery = 
"`"SELECT DISTINCT Hour," +
" cs-uri-stem," +
" SUM( cs-uri-hits ) as cs-uri-hits," +
" MIN( time-taken-min ) as time-taken-min," +
" MAX( time-taken-max ) as time-taken-max," +
" AVG( time-taken-avg ) as time-taken-avg" +
#" MIN( sc-bytes-min ) as sc-bytes-min," +
#" MAX( sc-bytes-max ) as sc-bytes-max," +
#" AVG( sc-bytes-avg ) as sc-bytes-avg," +
#" SUM( sc-bytes-sum ) as sc-bytes-sum," +
#" MIN( cs-bytes-min ) as cs-bytes-min," +
#" MAX( cs-bytes-max ) as cs-bytes-max," +
#" AVG( cs-bytes-avg ) as cs-bytes-avg," +
#" SUM( cs-bytes-sum ) as cs-bytes-sum" +
" INTO " + $uriHourUriStatsFile + 
" FROM " + $uriHourIpStatusStatsFile + 
" GROUP BY Hour," +
" cs-uri-stem" +
" ORDER BY Hour ASC`"" +
" -i: CSV -o: CSV"

$thirdQuery = 
"`"SELECT DISTINCT cs-uri-stem," +
" SUM( cs-uri-hits ) as cs-uri-hits," +
" MIN( time-taken-min ) as time-taken-min," +
" MAX( time-taken-max ) as time-taken-max," +
" AVG( time-taken-avg ) as time-taken-avg" +
#" MIN( sc-bytes-min ) as sc-bytes-min," +
#" MAX( sc-bytes-max ) as sc-bytes-max," +
#" AVG( sc-bytes-avg ) as sc-bytes-avg," +
#" SUM( sc-bytes-sum ) as sc-bytes-sum," +
#" MIN( cs-bytes-min ) as cs-bytes-min," +
#" MAX( cs-bytes-max ) as cs-bytes-max," +
#" AVG( cs-bytes-avg ) as cs-bytes-avg," +
#" SUM( cs-bytes-sum ) as cs-bytes-sum" +
" INTO " + $statsFile + 
" FROM " + $uriHourUriStatsFile + 
" GROUP BY cs-uri-stem`"" +
#" ORDER BY cs-uri-hits DESC`"" +
" -i: CSV -o: CSV"


#
# Pregenerate aggregate information from the log(s)
#
#$q = "`"SELECT QUANTIZE(TO_TIMESTAMP(date, time),3600) AS Hour, TO_LOWERCASE( cs-uri-stem ) as Uri, sc-status, sc-substatus, sc-win32-status, c-ip, COUNT( Uri ) as hitcount, MIN( time-taken ) as MinTimeTaken, MAX( time-taken ) as MaxTimeTaken, AVG(time-taken) as AvgTimeTaken INTO " + $statsFile + " FROM " + $LPPathToFile + " GROUP BY Hour ,Uri, sc-status, sc-substatus, sc-win32-status, c-ip ORDER BY Hour ASC`" -i: IISW3C -o: CSV"
#RunQuery -query $q -fWriteOutput $false

ExecuteQuery -queryStr $firstQuery
ExecuteQuery -queryStr $secondQuery
ExecuteQuery -queryStr $thirdQuery

#
#Query 1 - Total Requests Count
#

#$q =  " `"SELECT COUNT(*) AS Hits FROM " + $PathToFile + " TO " +  $outcsvfile  +  "`" -i:W3C -o:CSV" 
#$t =  "<h1>IIS Log Analysis Report</h1>
#       <h4>Generated on file " + [System.IO.Path]::GetFileName($PathToFile) + " (" + $dt + ") </h4>  
#	   <h4>==================================================================================</h4>
#	   <h2>Total Requests Count</h2>"
#RunQuery -query $q -title $t -queryout $outcsvfile


##
##Query 2 - Total Distinct Client IP Count
##
#$q =  " `"SELECT COUNT(DISTINCT c-ip) AS Counts FROM " + $PathToFile + " To " +   $outcsvfile + "`" -i:W3C -o:CSV" 
#$t =  "<H2>Total Distinct Client IP Count</H2>"
#RunQuery -query $q -title $t -queryout $outcsvfile


#
#Query 3 - Request Type Distribution
#
#$q =  " `"SELECT EXTRACT_EXTENSION(TO_LOWERCASE(cs-uri-stem)) AS ExtType, COUNT(*) INTO " + ( $outcsvfile + "-Q3.csv" ) + " FROM " + $LPPathToFile + " GROUP BY ExtType ORDER BY COUNT(*) DESC `" -i:W3C -o:CSV"
#$t =  "<a id=`"query3`"><h2>Request Type Distribution</h2>"
#RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q3.csv" )

#
# bugbug - this needs to handle cases like /page.aspx/string.
#
$q =  " `"SELECT EXTRACT_EXTENSION( cs-uri-stem ) AS ExtType, SUM( cs-uri-hits ) as Hits, MAX( time-taken-max ) as MaxTime, MIN( time-taken-min ) as MinTime, AVG( time-taken-avg ) as AvgTime INTO " + ( $outcsvfile + "-Q3.csv" ) + " FROM " + $statsFile + " GROUP BY ExtType ORDER BY Hits DESC `" -i:CSV -o:CSV"
$t =  "<a id=`"query3`"><h2>Request Type Distribution</h2></a>"
RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q3.csv" )
if (Test-Path ( $outcsvfile + "-Q3.csv" ))
{
   Remove-Item ($outcsvfile + "-Q3.csv")
}

#
#Query 18 Top 20 Longest Processing Requests
#
$q = "`"SELECT TOP 20 cs-uri-stem AS URI, date AS Date, time AS Time, sc-status, time-taken as TimeTaken(ms) INTO " + ( $outcsvfile + "-Q18.csv" ) + " FROM " + $LPPathToFile + " where   URI not LIKE '%daas%'  ORDER BY time-taken DESC `" -i:IISW3C -o:CSV"  
$t =  "<a id=`"query18`"><H2>Top 20 Longest Processing Requests</H2></a>"
RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q18.csv" )

if (Test-Path ( $outcsvfile + "-Q18.csv" ))
{
   Remove-Item ($outcsvfile + "-Q18.csv")
}

#
#Query 4 Top 20 Hits
#
#$q =  " `"SELECT TOP 20 TO_LOWERCASE(cs-uri-stem) AS URI, COUNT(*) AS Hits INTO " + ( $outcsvfile + "-Q4.csv" ) + " FROM " + $LPPathToFile + " GROUP BY URI ORDER BY Hits DESC `"  -i:W3C -o:CSV" 
#$t =  "<a id=`"query4`"><H2>Top 20 Hits</H2>"
#RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q4.csv" )

#$q =  " `"SELECT TOP 20 cs-uri-stem as Uri, SUM( cs-uri-hits ) AS Hits, MAX( time-taken-max ) as MaxTime, MIN( time-taken-min ) as MinTime, AVG( time-taken-avg ) as AvgTime INTO " + ( $outcsvfile + "-Q4.csv" ) + " FROM " + $statsFile + " GROUP BY URI ORDER BY Hits DESC `"  -i:CSV -o:CSV" 
$q =  " `"SELECT TOP 20 cs-uri-stem as Uri, cs-uri-hits AS Hits, time-taken-max as MaxTime, time-taken-min as MinTime, time-taken-avg as AvgTime INTO " + ( $outcsvfile + "-Q4.csv" ) + " FROM " + $statsFile + " where   URI not LIKE '%daas%' GROUP BY URI, Hits, MaxTime, MinTime, AvgTime ORDER BY Hits DESC `"  -i:CSV -o:CSV" 
$t =  "<a id=`"query4`"><H2>Top 20 Hits</H2></a>"
RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q4.csv" )

#
#Query 5 Top 20 Hits Chart
#
# Charts based on queries already run should use the output from previous query for perf.
#
#$t =  "<a id=`"query5`"><H2>Top 20 Hits Chart</H2></a>"
##$o =  Join-Path $ReportDirectory ("Top20Hits-" + $FileGUID + ".jpg")
#$o =  Join-Path $ReportDirectory ("Top20Hits-" + $FileGUID + ".csv")
##$q =  " `"SELECT URI, Hits INTO " + $o + " FROM " + ( $outcsvfile + "-Q4.csv" ) + "`" -i:CSV -o:CHART -charttype:ColumnClustered -groupsize:640x480 -chartTitle: `"Top 20 Hits Chart`"" 
#$q =  " `"SELECT URI, Hits INTO " + $o + " FROM " + ( $outcsvfile + "-Q4.csv" ) + "`" -i:CSV -o:CSV " 
#RunQuery -query $q -title $t -queryout $o 
#
#
#if (Test-Path ( $outcsvfile + "-Q4.csv" ))
#{
#   Remove-Item ($outcsvfile + "-Q4.csv")
#}
#
#if (Test-Path  $o)
#{
#   Remove-Item ($o)
#}

#
#Query 6 Top 20 ASPX Hits
#
#$q = "`"SELECT TOP 20 TO_LOWERCASE(cs-uri-stem) AS URI, COUNT(*) AS Hits INTO " + ( $outcsvfile + "-Q6.csv" ) + " FROM " +  $LPPathToFile + " WHERE URI LIKE '%.aspx%' GROUP BY URI ORDER BY Hits DESC `" -i:W3C -o:CSV"  
#$t =  "<a id=`"query6`"><H2>Top 20 ASPX Hits</H2>" 
#RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q6.csv" )

$q = "`"SELECT TOP 20 cs-uri-stem as Uri, cs-uri-hits AS Hits, time-taken-max as MaxTime, time-taken-min as MinTime, time-taken-avg as AvgTime INTO " + ( $outcsvfile + "-Q6.csv" ) + " FROM " +  $statsFile + " WHERE Uri LIKE '%.aspx%' GROUP BY Uri, Hits, MaxTime, MinTime, AvgTime ORDER BY Hits DESC `" -i:CSV -o:CSV"  
$t =  "<a id=`"query6`"><H2>Top 20 ASPX Hits</H2></a>" 
RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q6.csv" )

if (Test-Path ( $outcsvfile + "-Q6.csv" ))
{
   Remove-Item ($outcsvfile + "-Q6.csv")
}

#
#Query 7 Top 20 Slowest ASPX Hits
#
#$q = " `"SELECT TOP 20 TO_LOWERCASE(cs-uri-stem) as Uri, max(time-taken) as MaxTime, avg(time-taken) as AvgTime INTO " + ( $outcsvfile + "-Q7.csv" ) + " FROM " +  $LPPathToFile  + " WHERE Uri LIKE '%.aspx%' GROUP BY Uri ORDER BY MaxTime DESC`" -i:W3C -o:CSV" 
#$t =  "<a id=`"query7`"><H2>Top 20 Slowest ASPX Pages</H2>" 
#RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q7.csv" )

$q = " `"SELECT TOP 20 cs-uri-stem as Uri, cs-uri-hits AS Hits, time-taken-max as MaxTime, time-taken-min as MinTime, time-taken-avg as AvgTime INTO " + ( $outcsvfile + "-Q7.csv" ) + " FROM " +  $statsFile  + " WHERE Uri LIKE '%.aspx%' GROUP BY Uri, Hits, MaxTime, MinTime, AvgTime ORDER BY MaxTime DESC`" -i:CSV -o:CSV" 
$t =  "<a id=`"query7`"><H2>Top 20 Slowest ASPX Pages</H2></a>" 
RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q7.csv" )

if (Test-Path ( $outcsvfile + "-Q7.csv" ))
{
   Remove-Item ($outcsvfile + "-Q7.csv")
}

#
#Query 8 Top 20 ASMX Hits  - removed 04/11/14
#
##$q =  "`"SELECT TOP 20 TO_LOWERCASE(cs-uri-stem) AS URI, COUNT(*) AS Hits INTO " + ( $outcsvfile + "-Q8.csv" ) + "  FROM " + $LPPathToFile + " WHERE URI LIKE '%.asmx%' GROUP BY URI ORDER BY Hits DESC `" -i:W3C -o:CSV" 
##$t =  "<a id=`"query8`"><H2>Top 20 ASMX Hits </H2>"
##RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q8.csv" )
#
#$q =  "`"SELECT TOP 20 cs-uri-stem as Uri, cs-uri-hits AS Hits, time-taken-max as MaxTime, time-taken-min as MinTime, time-taken-avg as AvgTime INTO " + ( $outcsvfile + "-Q8.csv" ) + "  FROM " + $statsFile + " WHERE Uri LIKE '%.asmx%' GROUP BY URI, Hits, MaxTime, MinTime, AvgTime ORDER BY Hits DESC `" -i:CSV -o:CSV" 
#$t =  "<a id=`"query8`"><H2>Top 20 ASMX Hits </H2></a>"
#RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q8.csv" )
#
#if (Test-Path ( $outcsvfile + "-Q8.csv" ))
#{
#   Remove-Item ($outcsvfile + "-Q8.csv")
#}

#
#Query 9 Top 20 Client IP Addresses
#
#$q =  "`"SELECT TOP 20 c-ip, COUNT(*) AS Hits INTO " + ( $outcsvfile + "-Q9.csv" ) + " FROM " +  $LPPathToFile + " GROUP BY c-ip ORDER BY Hits DESC `" -i:W3C -o:CSV"  
#$t =  "<a id=`"query9`"><H2>Top 20 Client IP Addresses </H2>"
#RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q9.csv" )

$q =  "`"SELECT TOP 20 c-ip, SUM( cs-uri-hits ) AS Hits INTO " + ( $outcsvfile + "-Q9.csv" ) + " FROM " +  $uriHourIpStatusStatsFile + " GROUP BY c-ip ORDER BY Hits DESC `" -i:CSV -o:CSV"  
$t =  "<a id=`"query9`"><H2>Top 20 Client IP Addresses </H2></a>"
RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q9.csv" )


if (Test-Path ( $outcsvfile + "-Q9.csv" ))
{
   Remove-Item ($outcsvfile + "-Q9.csv")
}

#
#Query 10a Requests Per Hour table
#
# Hour should be in GMT as in the log, not in the UDE computer's local time.
#

$q = "`"SELECT Hour, SUM( cs-uri-hits ) AS Hits, MAX( time-taken-max ) as MaxTime, MIN( time-taken-min ) as MinTime, AVG( time-taken-avg ) as AvgTime INTO " + ( $outcsvfile + "-Q10a.csv" ) + " FROM " + $uriHourUriStatsFile + " GROUP BY Hour ORDER BY Hour`" -i:CSV -o:CSV"
$t =  "<a id=`"query10a`"><H2>Requests Per Hour Table</H2></a>"
RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q10a.csv" )

if (Test-Path ( $outcsvfile + "-Q10a.csv" ))
{
   Remove-Item ($outcsvfile + "-Q10a.csv")
}

#
#Query 11 HTTP Status Counts
#
#$q =  " `"SELECT DISTINCT sc-status AS Status, COUNT(*) AS Hits INTO " + ( $outcsvfile + "-Q11.csv" ) + " FROM " + $LPPathToFile + " GROUP BY Status ORDER BY Status ASC `" -i:W3C -o:CSV" 
#$t =  "<a id=`"query11`"><H2>HTTP Status Counts</H2>"
#RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q11.csv" )

$q =  " `"SELECT DISTINCT sc-status AS Status, SUM( cs-uri-hits ) AS Hits INTO " + ( $outcsvfile + "-Q11.csv" ) + " FROM " + $uriHourIpStatusStatsFile + " GROUP BY Status ORDER BY Status ASC `" -i:CSV -o:CSV" 
$t =  "<a id=`"query11`"><H2>HTTP Status Counts</H2></a>"
RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q11.csv" )

if (Test-Path ( $outcsvfile + "-Q11.csv" ))
{
   Remove-Item ($outcsvfile + "-Q11.csv")
}

#
#Query 13 Top 20 HTTP 304 Errors
#
#$q = "`"SELECT TOP 20 TO_LOWERCASE(cs-uri-stem) AS URI, sc-substatus, sc-win32-status, COUNT(*) AS Hits INTO " + ( $outcsvfile + "-Q13.csv" ) + " FROM " + $LPPathToFile + " WHERE sc-status = 304 GROUP BY URI, sc-substatus, sc-win32-status ORDER BY Hits DESC`" -i:W3C -o:CSV" 
#$t =  "<a id=`"query13`"><H2>Top 20 HTTP 304 Errors</H2>"
#RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q13.csv" )

$q = "`"SELECT TOP 20 cs-uri-stem as Uri, sc-substatus, sc-win32-status, SUM( cs-uri-hits ) as Hits INTO " + ( $outcsvfile + "-Q13.csv" ) + " FROM " + $uriHourIpStatusStatsFile + " WHERE sc-status = 304 GROUP BY URI, sc-substatus, sc-win32-status ORDER BY Hits DESC`" -i:CSV -o:CSV" 
$t =  "<a id=`"query13`"><H2>Top 20 HTTP 304 Errors</H2></a>"
RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q13.csv" )

if (Test-Path ( $outcsvfile + "-Q13.csv" ))
{
   Remove-Item ($outcsvfile + "-Q13.csv")
}

#
#Query 14 Top 20 HTTP 404 Errors
#
#$q = "`"SELECT TOP 20 TO_LOWERCASE(cs-uri-stem) AS URI, sc-substatus, sc-win32-status, COUNT(*) AS Hits INTO "+  ( $outcsvfile + "-Q14.csv" ) + " FROM " + $LPPathToFile + " WHERE sc-status = 404 GROUP BY URI, sc-substatus, sc-win32-status ORDER BY Hits DESC`" -i:W3C -o:CSV" 
#$t =  "<a id=`"query14`"><H2>Top 20 HTTP 404 Errors</H2>"
#RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q14.csv" )

$q = "`"SELECT TOP 20 cs-uri-stem as Uri, sc-substatus, sc-win32-status, SUM( cs-uri-hits ) as Hits INTO " + ( $outcsvfile + "-Q14.csv" ) + " FROM " + $uriHourIpStatusStatsFile + " WHERE sc-status = 404 GROUP BY URI, sc-substatus, sc-win32-status ORDER BY Hits DESC`" -i:CSV -o:CSV" 
$t =  "<a id=`"query14`"><H2>Top 20 HTTP 404 Errors</H2></a>"
RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q14.csv" )

if (Test-Path ( $outcsvfile + "-Q14.csv" ))
{
   Remove-Item ($outcsvfile + "-Q14.csv")
}

#
#Query 15 Top 20 HTTP 403 Errors
#
#$q = "`"SELECT TOP 20 TO_LOWERCASE(cs-uri-stem) AS URI, sc-substatus, sc-win32-status, COUNT(*) AS Hits INTO "+  ( $outcsvfile + "-Q15.csv" ) + " FROM " + $LPPathToFile + " WHERE sc-status = 403 GROUP BY URI, sc-substatus, sc-win32-status ORDER BY Hits DESC`" -i:W3C -o:CSV" 
#$t =  "<a id=`"query15`"><H2>Top 20 HTTP 403 Errors</H2>"
#RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q15.csv" )

$q = "`"SELECT TOP 20 cs-uri-stem as Uri, sc-substatus, sc-win32-status, SUM( cs-uri-hits ) as Hits INTO " + ( $outcsvfile + "-Q15.csv" ) + " FROM " + $uriHourIpStatusStatsFile + " WHERE sc-status = 403 GROUP BY URI, sc-substatus, sc-win32-status ORDER BY Hits DESC`" -i:CSV -o:CSV" 
$t =  "<a id=`"query15`"><H2>Top 20 HTTP 403 Errors</H2></a>"
RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q15.csv" )

if (Test-Path ( $outcsvfile + "-Q15.csv" ))
{
   Remove-Item ($outcsvfile + "-Q15.csv")
}

#
#Query 16 Top 20 HTTP 500 Errors
#
#$q = "`"SELECT TOP 20 TO_LOWERCASE(cs-uri-stem) AS URI, sc-substatus, sc-win32-status, COUNT(*) AS Hits INTO "+  ( $outcsvfile + "-Q16.csv" ) + " FROM " + $LPPathToFile + " WHERE sc-status = 500 GROUP BY URI, sc-substatus, sc-win32-status ORDER BY Hits DESC`" -i:W3C -o:CSV" 
#$t =  "<a id=`"query16`"><H2>Top 20 HTTP 500 Errors</H2>"
#RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q16.csv" )

$q = "`"SELECT TOP 20 cs-uri-stem as Uri, sc-substatus, sc-win32-status, SUM( cs-uri-hits ) as Hits INTO " + ( $outcsvfile + "-Q16.csv" ) + " FROM " + $uriHourIpStatusStatsFile + " WHERE sc-status = 500 GROUP BY URI, sc-substatus, sc-win32-status ORDER BY Hits DESC`" -i:CSV -o:CSV" 
$t =  "<a id=`"query16`"><H2>Top 20 HTTP 500 Errors</H2></a>"
RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q16.csv" )

if (Test-Path ( $outcsvfile + "-Q16.csv" ))
{
   Remove-Item ($outcsvfile + "-Q16.csv")
}

#
#Query 17 Top 20 HTTP 503 Errors
#
#$q = "`"SELECT TOP 20 TO_LOWERCASE(cs-uri-stem) AS URI, sc-substatus, sc-win32-status, COUNT(*) AS Hits INTO "+  ( $outcsvfile + "-Q17.csv" ) + " FROM " + $LPPathToFile + " WHERE sc-status = 503 GROUP BY URI, sc-substatus, sc-win32-status ORDER BY Hits DESC`" -i:W3C -o:CSV" 
#$t =  "<a id=`"query17`"><H2>Top 20 HTTP 503 Errors</H2>"
#RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q17.csv" )

$q = "`"SELECT TOP 20 cs-uri-stem as Uri, sc-substatus, sc-win32-status, SUM( cs-uri-hits ) as Hits INTO " + ( $outcsvfile + "-Q17.csv" ) + " FROM " + $uriHourIpStatusStatsFile + " WHERE sc-status = 503 GROUP BY URI, sc-substatus, sc-win32-status ORDER BY Hits DESC`" -i:CSV -o:CSV" 
$t =  "<a id=`"query17`"><H2>Top 20 HTTP 503 Errors</H2></a>"
RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q17.csv" )

if (Test-Path ( $outcsvfile + "-Q17.csv" ))
{
   Remove-Item ($outcsvfile + "-Q17.csv")
}


#
#Query 19  Top 100 Longest Requests from PingDOM agent 
#
$q = "`"SELECT TOP 100 cs-uri-stem AS URI, sc-status, time-taken as TimeTaken(ms) INTO " + ( $outcsvfile + "-Q19.csv" ) + " FROM " + $LPPathToFile +  " WHERE cs(User-Agent) LIKE '%PINGDOM%' ORDER BY time-taken DESC`" -i:IISW3C -o:CSV"  
$t =  "<a id=`"query19`"><H2>Top 100 Longest Requests from PINGDOM agent</H2></a>"
RunQuery -query $q -title $t -queryout ( $outcsvfile + "-Q19.csv" )

if (Test-Path ( $outcsvfile + "-Q19.csv" ))
{
   Remove-Item ($outcsvfile + "-Q19.csv")
}

Out-File -FilePath $outhtmlfile -Append -InputObject $htmlClose

if (Test-Path $uriHourIpStatusStatsFile)
{
    Remove-Item $uriHourIpStatusStatsFile
}

if (Test-Path $uriHourUriStatsFile)
{
    Remove-Item $uriHourUriStatsFile
}

if (Test-Path $statsFile)
{
    Remove-Item $statsFile
}

#we need to copy the \script folder to the folder where html file is
$destScriptFolder = Join-Path $ReportDirectory  "script"
$srcScriptFolder = Join-Path $Script:ScriptPath "script"

if (Test-Path $srcScriptFolder)
{
	Log ("script destination folder doesn't exist. Create folder and copy all files" )

	#We cannot use copy-item -recurse here to do a folder copy , since the copying maybe happen in parallel from different instances of this
	#program
	#Copy-Item  $srcScriptFolder $ReportDirectory -Recurse -ErrorAction SilentlyContinue	
					
	$File =  Get-ChildItem -Path $srcScriptFolder -File 
	foreach($f in $File)
	{	
	    try
		{

			if ( (Test-Path $destScriptFolder) -eq $false)
			{
			    New-Item $destScriptFolder -ItemType directory 
			}
					 
			$desfFile = Join-Path -Path $destScriptFolder -ChildPath $f.Name
				
			if ( (Test-Path -Path $desfFile) -eq $false)
			{   
		    	Copy-Item  $f.FullName  $destScriptFolder 
			}
		}
		catch 
		{
			$Detail = $Error[0].Exception.Message
		    Log ("Exception on copy-item: $Detail" )
		}
	}

}
else
{
	Log ("script source folder doesn't exist. we don't do anything.." )
}

Log ("Report File Generated: " + $outhtmlfile)


