use strict;
use warnings;
use File::Copy;
use File::Basename;
    
sub printArray 
{
  my @array = @_;
  foreach my $entry (@array)
  {
    print "  $entry\n";
  }
}

sub formatTimeLikeHttpLogs
{
    my $timeStr = shift;
    my @timeElements = split (/[:T-]/, $timeStr);
    my $httpTimeStr = "$timeElements[0]$timeElements[1]$timeElements[2]$timeElements[3]$timeElements[4]";
    return $httpTimeStr;
}

sub getFileDateTime
{
    my $fileName = shift;
    my @fileParts = split(/[.-]/, $fileName);
    return $fileParts[1];
}

my $outputDir = $ARGV[0];
if (!defined $outputDir || $outputDir eq "")
{
    die "Need to pass the output directory";
}

my $startDateTime = $ARGV[1];
if (!defined $startDateTime || $startDateTime eq "")
{
    die "Need to pass the start DateTime";
}
$startDateTime = formatTimeLikeHttpLogs($startDateTime);
print "Start dateTime is $startDateTime\n";

my $endDateTime = $ARGV[2];
if (!defined $endDateTime || $endDateTime eq "")
{
    die "Need to pass the end DateTime";
}
$endDateTime = formatTimeLikeHttpLogs($endDateTime);
print "End dateTime is $endDateTime\n";

my $siteRoot = $ENV{"HOME_EXPANDED"};
my $httpLogDir = $siteRoot . "/LogFiles/http/RawLogs/";

my $instanceName = $ENV{"WEBSITE_INSTANCE_ID"};
my $instanceUsedForLogs = substr ($instanceName, 0, 6);


unless (-d $httpLogDir)
{
    print "Http log directory not found. Do you need to enable Http logging for your website?";
    exit;
}

opendir(DIR, $httpLogDir) or die $!;

my @httpLogFiles
    = grep { 
        /$instanceUsedForLogs.*\.log/   # Get http logs for this instance only
  && -f "$httpLogDir/$_"   # and is a file
} readdir(DIR);

closedir(DIR);

if (scalar @httpLogFiles == 0)
{
  print "Did not find any http logs\n";
  exit 0;
}

# Sort log files in descending order so that the newest is on top
@httpLogFiles = sort { $b cmp $a } @httpLogFiles;
print "Log files found:\n";
printArray(@httpLogFiles);

# We only want to copy http logs that might contain logs from the requested time range.
# Since the files are sorted in reverse order, copy all the files up to and including the first log
#    that started before the requested start time
my @logFilesToCopy = ();
for (my $i = 0; $i <= $#httpLogFiles; $i++)
{
    my $file = $httpLogFiles[$i];
    my $fileDateTime = getFileDateTime($file);
    print "Checking $fileDateTime\n";
    if ($fileDateTime > $endDateTime)
    {   
        next; # This log started after the period which we care about
    }
    push(@logFilesToCopy, $file);
    if ($fileDateTime < $startDateTime)
    {
        last; # This is the last log file which could contain any of the requested http logs
    }
}

print "Going to copy these logs\n";
printArray(@logFilesToCopy);

foreach my $httpLog (@logFilesToCopy)
{
    print "Copying http log $httpLog\n";

    my $httpLogName = fileparse($httpLog);
    #print "Http log file name is $httpLogName\n";

    my $httpLogSource = "$httpLogDir$httpLog";
    my $httpLogDestination = "$outputDir/$httpLogName";

    #print "Source is $httpLogSource\n";
    #print "Destination will be $httpLogDestination\n";

    copy ($httpLogSource, $httpLogDestination) or die "Copy failed: $!";
    print "Copied file\n";
}