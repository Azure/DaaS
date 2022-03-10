$javaRunning = (Get-Process -Name java -ErrorAction SilentlyContinue)

if ($javaRunning)
{
	"java.exe is running"
	exit 0
}
else
{
	"Found no java.exe running for this web app"
	exit -1
}