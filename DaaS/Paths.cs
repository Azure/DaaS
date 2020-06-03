using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DaaS.Sessions;

namespace DaaS
{
    class Paths
    {
        public static string GetRelativeSessionDir(SessionStatus sessionStatus)
        {
            switch (sessionStatus)
            {
                case SessionStatus.Complete:
                case SessionStatus.Cancelled:
                case SessionStatus.Error:
                    return SessionDirectories.CompletedSessionsDir;
                case SessionStatus.CollectedLogsOnly:
                    return SessionDirectories.CollectedLogsOnlySessionsDir;
                case SessionStatus.Active:
                    return SessionDirectories.ActiveSessionsDir;
                default:
                    throw new Exception("Umm...I don't recognize this session status");
            }
        }
    }
}
