// -----------------------------------------------------------------------
// <copyright file="ISessionManager.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DaaS.Configuration;

namespace DaaS.Sessions
{
    /// <summary>
    /// Interface for interacting with diagnostic sessions
    /// </summary>
    public interface ISessionManager
    {
        /// <summary>
        /// Submit a new DaaS session
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        Task<string> SubmitNewSessionAsync(Session session, bool isV2Session, bool invokedViaDaasConsole = false);

        /// <summary>
        /// Lists all DaaS sessions, complete as well as active
        /// </summary>
        /// <returns></returns>
        Task<IEnumerable<Session>> GetAllSessionsAsync(bool isDetailed = false);

        /// <summary>
        /// Lists all the complete DaaS sessions
        /// </summary>
        /// <returns></returns>
        Task<IEnumerable<Session>> GetCompletedSessionsAsync(bool isV2Session);

        /// <summary>
        /// Get a specific DaaS session
        /// </summary>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        Task<Session> GetSessionAsync(string sessionId, bool isDetailed = false);
        bool IsSandboxAvailable();

        /// <summary>
        /// Gets the active diagnostic session object
        /// </summary>
        /// <returns></returns>
        Task<Session> GetActiveSessionAsync(bool isV2Session, bool isDetailed = false);

        /// <summary>
        /// Once a DaaS session is submitted, this method should be
        /// called to run the diagnostic tool specified in the session
        /// </summary>
        /// <param name="activeSession"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task RunToolForSessionAsync(Session activeSession,bool isV2Session, bool queueAnalysisRequest, CancellationToken token);

        /// <summary>
        /// Checks if the current instance is specified in the list of instances to collect
        /// data from the diagnostic session
        /// </summary>
        /// <param name="activeSession"></param>
        /// <returns></returns>
        bool ShouldCollectOnCurrentInstance(Session activeSession);

        /// <summary>
        /// Checks if session has a status of AnalysisQueued for the current instance
        /// </summary>
        /// <param name="activeSession"></param>
        /// <returns></returns>
        bool ShouldAnalyzeOnCurrentInstance(Session activeSession);

        /// <summary>
        /// Checks if the current instance has already collected the logs for the diagnostic
        /// session or not
        /// </summary>
        /// <returns></returns>
        Task<bool> HasThisInstanceCollectedLogs(bool isV2Session);

        /// <summary>
        /// Marks a diagnostic session as complete if all instances have finished collecting
        /// data
        /// </summary>
        /// <param name="forceCompletion"></param>
        /// <returns></returns>
        Task<bool> CheckandCompleteSessionIfNeededAsync(bool isV2Session, bool forceCompletion = false);

        /// <summary>
        /// Gets the diagnostic tool from the session. Validates the tool params and throws an exception if an
        /// invalid value is passed
        /// </summary>
        /// <param name="activeSession"></param>
        /// <returns></returns>

        Task DeleteSessionAsync(string sessionId, bool isV2Session);

        /// <summary>
        /// Checks if a Session exists or not
        /// </summary>
        /// <param name="sessionId"></param>
        /// <returns></returns>

        bool IsSessionExisting(string sessionId, bool isV2Session);

        /// <summary>
        /// Used to Cancel all those instances that have not picked up the session within the desired timeout
        /// </summary>
        /// <returns></returns>
        Task CancelOrphanedInstancesIfNeeded(bool isV2Session);

        /// <summary>
        /// Get all diagnosers from the DaaS configuration
        /// </summary>
        /// <returns></returns>
        List<DiagnoserDetails> GetDiagnosers();


        /// <summary>
        /// Validates the configured Storage account. Used in Hyper-V scenarios
        /// </summary>
        /// <returns></returns>
        Task<StorageAccountValidationResult> ValidateStorageAccount();

        /// <summary>
        /// Updates the storage account connection if end user configures a new storage account or changes an existing one. Used in Hyper-V scenarios.
        /// </summary>
        /// <param name="storageAccount">Connection string of storage account</param>
        /// <returns>Bool indicating if storage account was configured correctly.</returns>
        Task<bool> UpdateStorageAccount(StorageAccount storageAccount);

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
        /// Analyzes and completes the session
        /// </summary>
        /// <param name="activeSession"></param>
        /// <param name="isV2Session"></param>
        /// <param name="sessionId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task AnalyzeAndCompleteSessionAsync(Session activeSession, bool isV2Session, string sessionId, CancellationToken token);

        /// <summary>
        /// Checks if any instance is analyzing the diagnostic session
        /// </summary>
        /// <param name="activeSession"></param>
        /// <returns></returns>
        bool CheckIfAnalysisQueuedForCurrentInstance(Session activeSession);

        /// <summary>
        /// Completes a stuck session if needed
        /// </summary>
        /// <param name="activeSession"></param>
        /// <returns></returns>
        Task CheckIfOrphaningOrTimeoutNeededAsync(Session activeSession);

        /// <summary>
        /// This property decides whether SAS URI will be included in the 
        /// Session object or not.
        /// </summary>
        bool IncludeSasUri { get; set; }

        bool InvokedViaAutomation { get; set; }
    }
}
