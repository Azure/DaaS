use strict;
use warnings;
use File::Copy;
use File::Basename;

my $logFile = $ARGV[0];
my $outputDir = $ARGV[1];
if (!defined $outputDir || $outputDir eq "")
{
    die "Need to pass the log file and output directory";
}

my $logFileWithSwaps = $logFile;
$logFileWithSwaps =~ s/\\/\//g;
my $logFileName = basename($logFileWithSwaps);
my $reportFile = $outputDir . "/Report_$logFileName.report";
copy ($logFile, $reportFile) or die "Copy $logFile to $reportFile failed: $!";
