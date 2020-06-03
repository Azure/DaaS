$loggingEnabled = $Env:WEBSITE_HTTPLOGGING_ENABLED

if ($loggingEnabled -eq 1)
{
	"Logging is enabled"
	exit 0
}
else
{
	"Logging is not enabled"
	exit -1
}