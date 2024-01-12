// -----------------------------------------------------------------------
// <copyright file="IAzureStorageSessionManager.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace DaaS.Sessions
{
    public interface IAzureStorageSessionManager : ISessionManager
    {
        /// <summary>
        /// Identifies whether IAzureStorageSessionManager is valid or not
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Starts DaasLauncher.exe process to submit the session
        /// </summary>
        /// <param name="args"></param>
        /// <param name="sessionId"></param>
        /// <param name="description"></param>
        Process StartDiagLauncher(string args, string sessionId, string description);

        /// <summary>
        /// Throws an exception if multiple instances of DaasLauncher.exe are running
        /// </summary>
        /// <param name="processId"></param>
        void ThrowIfMultipleDiagLauncherRunning(int processId);

        /// <summary>
        /// Runs the active diagnostic sessionV2
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        Task RunActiveSessionAsync(bool queueAnalysisRequest, CancellationToken token);

        /// <summary>
        /// Checks if session has a status of AnalysisQueued for the current instance
        /// </summary>
        /// <param name="activeSession"></param>
        /// <returns></returns>
        bool ShouldAnalyzeOnCurrentInstance(Session activeSession);

        /// <summary>
        /// Analyzes and completes the session
        /// </summary>
        /// <param name="activeSession"></param>
        /// <param name="isV2Session"></param>
        /// <param name="sessionId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task AnalyzeAndCompleteSessionAsync(Session activeSession, string sessionId, CancellationToken token);

        /// <summary>
        /// Checks if any instance is analyzing the diagnostic session
        /// </summary>
        /// <param name="activeSession"></param>
        /// <returns></returns>
        bool CheckIfAnalysisQueuedForCurrentInstance(Session activeSession);

        /// <summary>
        /// Checks a V2 session to see if it should be timed out and returns true if the session was timed out or completed
        /// </summary>
        /// <param name="activeSession"></param>
        /// <returns></returns>
        Task<bool> ShouldSessionTimeoutAsync(Session activeSession);

        /// <summary>
        /// Checks if the Instance is orphaned and cancels it if needed 
        /// </summary>
        /// <param name="activeSession"></param>
        /// <returns></returns>
        Task<bool> CancelOrphanedV2InstancesIfNeeded(Session activeSession);
    }
}
