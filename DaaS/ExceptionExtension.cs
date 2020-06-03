using System;

namespace DaaS
{
    public static class ExceptionExtension
    {
        public static string ToLogString(this Exception exception)
        {
            if (exception == null)
            {
                return string.Empty;
            }
            else
            {
                string exceptionLog =  $"{exception.GetType().ToString()}:{exception.Message} {Environment.NewLine} {exception.StackTrace}";
                if (exception.InnerException != null)
                {
                    exceptionLog = $"{exceptionLog}{Environment.NewLine}InnerException={exception.InnerException.GetType().ToString()}:{exception.InnerException.Message} {Environment.NewLine} {exception.InnerException.StackTrace} ";
                }

                return exceptionLog;
            }
        }
    }
}
