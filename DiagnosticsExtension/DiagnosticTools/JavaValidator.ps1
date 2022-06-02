$javaRunning = (Get-Process -Name java -ErrorAction SilentlyContinue)

if ($javaRunning)
{
	"java.exe is running"
	exit 0
}
else
{
    $javaRunning = (Get-Process -Name javaw -ErrorAction SilentlyContinue)
    if ($javaRunning)
    {
        "javaw.exe is running"
        exit 0
    }
    else
    {
        "Found no java.exe or javaw.exe running for this web app"
        exit -1
    }
}