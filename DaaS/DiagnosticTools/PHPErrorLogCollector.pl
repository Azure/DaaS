use strict;
use warnings;
use File::Copy;

my $outputDir;
my $checkExists = 0;

foreach (@ARGV)
{
  if ($_ eq "-checkExists")
  {
    $checkExists = 1;
  }
  else
  {
    $outputDir = $_;
  }
}

if (!$checkExists && (!defined $outputDir || $outputDir eq ""))
{
    die "Need to pass the output directory";
}

my $phpErrorLogFile = "php_errors.log";

my $siteRoot = $ENV{"HOME_EXPANDED"};
my $phpErrorLogFileSource = $siteRoot . "/LogFiles/" . $phpErrorLogFile;

my $PHPErrorLogFileDestination;
if (!$checkExists)
{
  $PHPErrorLogFileDestination = $outputDir . "/" . $phpErrorLogFile;
}

if ($checkExists)
{
  # Return 0 to say php error logs exist, anything else to say it doesn't
  if (-e $phpErrorLogFileSource)
  {
    exit 0;
  }
  else
  {
    exit -1;
  }
}
else
{
  copy ($phpErrorLogFileSource, $PHPErrorLogFileDestination) or die "Copy failed: $!";
}
