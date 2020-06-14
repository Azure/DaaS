//-----------------------------------------------------------------------
// <copyright file="RetryHelper.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;

namespace DaaS
{
    public static class RetryHelper
    {
        public static void RetryOnException(string actionInfo, Action operation, TimeSpan delay, int times = 3, bool logAllExceptions = true, bool throwAfterRetry = true)
        {
            var attempts = 0;
            do
            {
                try
                {
                    attempts++;
                    operation();
                    break; // Sucess! Lets exit the loop!
                }
                catch (Exception ex)
                {
                    if (attempts == times)
                    {
                        if (logAllExceptions)
                        {
                            Logger.LogErrorEvent($"Operation:{actionInfo} failed after {times} retries", ex);
                        }
                        else
                        {
                            if (!(ex is IOException) && !(ex is UnauthorizedAccessException))
                            {
                                Logger.LogErrorEvent($"Operation:{actionInfo} failed after {times} retries", ex);
                            }
                        }
                        if (throwAfterRetry)
                        {
                            throw;
                        }
                    }
                    Task.Delay(delay).Wait();
                }
            } while (true);
        }

        public static async Task RetryOnExceptionAsync(int times, TimeSpan delay, Func<Task> operation)
        {
            await RetryOnExceptionAsync<Exception>(times, delay, operation);
        }

        public static async Task RetryOnExceptionAsync<TException>(int times, TimeSpan delay, Func<Task> operation) where TException : Exception
        {
            if (times <= 0)
                throw new ArgumentOutOfRangeException(nameof(times));

            var attempts = 0;
            do
            {
                try
                {
                    attempts++;
                    await operation();
                    break;
                }
                catch (TException ex)
                {
                    if (attempts == times)
                        throw;

                    await Task.Delay(delay);
                }
            } while (true);
        }
    }
}
