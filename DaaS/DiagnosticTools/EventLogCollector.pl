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

my $eventManifestFile = "eventlog.xml";

my $siteRoot = $ENV{"HOME_EXPANDED"};
my $eventManifestSource = $siteRoot . "/LogFiles/" . $eventManifestFile;

my $eventManifestDestination;
if (!$checkExists)
{
  $eventManifestDestination = $outputDir . "/" . $eventManifestFile;
}

if ($checkExists)
{
  # Return 0 to say event manifest logs exist, anything else to say it doesn't
  if (-f $eventManifestSource)
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
  copy ($eventManifestSource, $eventManifestDestination) or die "Copy failed: $!";
}
