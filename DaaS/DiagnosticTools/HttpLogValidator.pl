use strict;
use warnings;
use File::Copy;
use File::Basename;
    
my $httpLogEnabled = $ENV{"WEBSITE_HTTPLOGGING_ENABLED"};

print "Http log enabled flag is $httpLogEnabled\n";

if ($httpLogEnabled eq "1")
{
   exit 0;
}
else
{
   exit -1; 
}