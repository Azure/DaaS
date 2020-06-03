use strict;
use warnings;
use File::Copy;
use File::Find;

my $dumpAnalyzerDir = $ARGV[0];
if (!defined $dumpAnalyzerDir || $dumpAnalyzerDir eq "")
{
    die "Need to pass the directory containing DumpAnalyzer.exe";
}

my $diagnosticToolsArgs = $ARGV[1];
if (!defined $diagnosticToolsArgs || $diagnosticToolsArgs eq "")
{
    die "Need to pass the diagnosticTools Arguments";
}
my $tempDir = $ARGV[2];
if (!defined $tempDir || $tempDir eq "")
{
    die "Need to pass the tempDir";
}

my $eventManifestFile = "eventlog.xml";

$tempDir =~ s/\//\\/gi;
$tempDir .= "\\DumpAnalyzer\\";
$dumpAnalyzerDir =~ s/\//\\/gi;

my $cmd;

if (!-f "$tempDir\\DumpAnalyzer.exe")
{
    $cmd = "xcopy \"$dumpAnalyzerDir\" $tempDir";
    print "Copy command is $cmd\n";
    system ("xcopy", $dumpAnalyzerDir, $tempDir); # or die "Could not copy the dump analyzer: $!";
}

$diagnosticToolsArgs =~ s/\\/\\\\/g;
$cmd = "\"$dumpAnalyzerDir\\DumpAnalyzer.exe\" $diagnosticToolsArgs";
print "Command is $cmd\n";
system ( $cmd );