var app = angular.module('app', ['angular.filter']);

var knownIssues = ['Session', 'CLRThreadPoolQueue', 'GENERAL_FLUSH_RESPONSE', 'GENERAL_READ_ENTITY',
    'iisnode', 'AspNetCoreModule', 'AspNetCoreModuleV2', 'CgiModule', 'FastCgiModule', 'RewriteModule', 'httpPlatformHandler', 'AspNetReqSessionData'];

app.filter('extractUrl', function () {
    return function (input) {
        var pattern = /(http[s]?:\/\/)?([^\/\s]+\/)(.*)/g;
        var match = pattern.exec(input);


        if (match.length >= 3) {
            input = '/' + match[3];

            if (input.indexOf('?') > 0) {
                input = input.substring(0, input.indexOf('?'));
            }
        }
        return input;
    };
});

app.filter('getNotificationName', function () {
    return function (input) {

        return getNotification(input);
    };
});


app.controller('Ctrl', function Ctrl($scope, $http, $filter) {

    $scope.viewer = 'CallStack';

    var url = "reportdata/requests.json";

    $http.get(url).then(function (response) {
        $scope.requests = response.data;
        url = "reportdata/traceInfo.json";

        $http.get(url).then(function (response) {
            $scope.traceInformation = response.data;

            var ctx = document.getElementById('myChart').getContext('2d');
            var chart = new Chart(ctx, {
                type: 'pie',
                data: {
                    labels: $.map($scope.traceInformation.ModuleExecutionPercent, function (el) { return el.ModuleName; }),
                    datasets: [{
                        label: "Pipeline Stage",
                        data: $.map($scope.traceInformation.ModuleExecutionPercent, function (el) { return el.Percent; }),
                        backgroundColor: [
                            'rgba(255, 99, 132, 0.8)',
                            'rgba(54, 162, 235, 0.8)',
                            'rgba(255, 206, 86, 0.8)',
                            'rgba(75, 192, 192, 0.8)',
                            'rgba(153, 102, 255, 0.8)',
                            'rgba(255, 159, 64, 0.8)'
                        ]
                    }]
                },
                options: {
                    legend: {
                        display: false
                    },
                    legendCallback: function (chart) {
                        var text = [];
                        text.push("<ul style='white-space:nowrap;width:320px;overflow-x:auto;height:140px;overflow-y:auto;max-height:140px;padding:0;list-style:none' class='" + chart.id + "'-legend'>");

                        var data = chart.data;
                        var datasets = data.datasets;
                        var labels = data.labels;

                        if (datasets.length) {
                            for (var i = 0; i < datasets[0].data.length; ++i) {
                                text.push('<li style="padding:5px; font-size:smaller;"><div style="float:left;min-width:50px;max-width:50px;min-height:15px;background-color:' + datasets[0].backgroundColor[i] + '"></div>');
                                if (labels[i]) {
                                    text.push('&nbsp;&nbsp;' + labels[i] + '  [' + datasets[0].data[i] + '%] </div>');
                                }
                                text.push('</li>');
                            }
                        }
                        text.push('</ul>');
                        return text.join('');
                    },
                    pieceLabel: {
                        render: function (args) {
                            return args.value + '%';
                        },
                        fontColor: 'whitesmoke', precision: 2
                    },
                    layout: {
                        padding: {
                            left: 10, right: 0, top: 0, bottom: 0
                        }
                    },
                    responsive: false,
                    maintainAspectRatio: false
                }
            });

            document.getElementById('chart-legends').innerHTML = chart.generateLegend();

            var totalTimeSpent = $scope.traceInformation.TotalTimeInRequestExecution;

            var moduleBreakup = { Platform: 0, Application: 0, Network: 0 };

            $scope.traceInformation.ModuleExecutionPercent.forEach(function (module) {
                if ($scope.GetIssueType(module.ModuleName) === "application") {
                    moduleBreakup.Application += module.TimeSpent;
                }
                else if ($scope.GetIssueType(module.ModuleName) === "network") {
                    moduleBreakup.Network += module.TimeSpent;
                }
                else if ($scope.GetIssueType(module.ModuleName) === "platform") {
                    moduleBreakup.Platform += module.TimeSpent;
                }
            });

            $('#liApp').text(((moduleBreakup.Application / totalTimeSpent) * 100).toFixed(2) + '%');
            $('#liPlat').text(((moduleBreakup.Platform / totalTimeSpent) * 100).toFixed(2) + '%');
            $('#liWire').text(((moduleBreakup.Network / totalTimeSpent) * 100).toFixed(2) + '%');

            $('div[title!=""]').qtip({
                style: { classes: 'qtip-bootstrap' }
            });

            $('span[title!=""]').qtip({
                style: { classes: 'qtip-bootstrap' }
            });

            $('img[title!=""]').qtip({
                style: { classes: 'qtip-bootstrap' }
            });

        });
    });

    url = "reportdata/failedrequests.json";
    $http.get(url).then(function (response) {
        $scope.failedrequests = response.data;

    });

    url = "reportdata/processCpuInfo.json";
    $http.get(url).then(function (response) {
        $scope.processes = response.data;

    });

    url = "reportdata/stacks.json";
    $http.get(url).then(function (response) {
        $scope.rawStacks = response.data;

    });

    url = "reportdata/allexceptions.json";
    $http.get(url).then(function (response) {
        $scope.allExceptions = response.data;

    });

    url = "reportdata/allexceptionsoutproc.json";
    $http.get(url).then(function (response) {
        $scope.allExceptionsOutProc = response.data;

    });

    url = "reportdata/corerequests.json";
    $http.get(url).then(function (response) {
        $scope.corerequests = response.data;

    });

    url = "reportdata/corefailedrequests.json";
    $http.get(url).then(function (response) {
        $scope.coreFailedRequests = response.data;

    });

    $scope.showCpuStacks = function (process) {
        $scope.selectedCpuProcess = process;
    };

    $scope.getDiffIncompleteRequest = function (endTime, detailsCoreRequest) {
        var retVal = 0;
        if (detailsCoreRequest.length > 0 && endTime > 0) {
            var lastEvent = parseFloat(detailsCoreRequest[detailsCoreRequest.length - 1].TimeStampRelativeMSec);
            retVal = endTime - lastEvent;
        }

        if (retVal < 0) {
            retVal = 0
        }
        retVal = retVal.toFixed(0);
        return retVal;

    };
    $scope.getTimeStampMilliseconds = function (timeStamp) {
        var currentValue = parseFloat(timeStamp);

        if (!$scope.lastValue) {
            $scope.lastValue = 0;
        }
        if ($scope.lastValue === 0) {
            $scope.lastValue = currentValue;
        }

        var diff = currentValue - $scope.lastValue;
        $scope.lastValue = currentValue;

        var str = "0 ms";
        if (diff.toFixed(0) > 0) {
            str = "+" + diff.toFixed(0) + " ms";
        }

        return str;
    };

    $scope.updateCpuStacks = function () {

        if (!$scope.selectedCpuProcess) {
            $('#cpuStacksViewerTreeDiv').jstree("destroy");
            return;
        }


        var processId = $scope.selectedCpuProcess.Id;
        var processName = $scope.selectedCpuProcess.Name;

        url = "reportdata/cpuStacksjmc-" + processName + "-" + processId + ".json";

        var justMyCode = !document.getElementById("cbJustMyCodeCPU").checked;

        if (justMyCode === false) {
            url = "reportdata/cpuStacks-" + processName + "-" + processId + ".json";
        }

        $http.get(url).then(function (response) {

            var transformedJson = transFormJson(response.data, 0);

            $('#cpuStacksViewerTreeDiv').jstree("destroy");

            var cpustackTraceTree = $('#cpuStacksViewerTreeDiv').jstree({
                plugins: ["grid"],
                grid: {
                    columns: [
                        { minWidth: 1000, header: "Name" },
                        { header: "Total CPU %", value: "InclusiveMetricPercent" },
                        { header: "Total CPU ms", value: "TimeSpent" },
                        { header: "Self CPU ms", value: "ExclusiveTime" }
                    ],
                    resizable: true
                },
                'core': {
                    "themes": {
                        "variant": "small",
                        "icons": false
                    },
                    'data': function (obj, callback) {
                        callback.call(this, transformedJson);
                    }
                }
            });
        }, function (response) { // error has occured
            $('#cpuStacksViewerTreeDiv').jstree("destroy");
        });
    };

    $scope.updateCpuStacks();

    $scope.activeRequest = null;
    $scope.activeFailedRequest = null;


    $scope.GetIssueType = function (moduleName) {
        var iisModules = ['UriCacheModule', 'FileCacheModule', 'TokenCacheModule', 'HttpCacheModule', 'DynamicCompressionModule', 'StaticCompressionModule',
            'DefaultDocumentModule', 'DirectoryListingModule', 'ProtocolSupportModule', 'ServerSideIncludeModule',
            'StaticFileModule', 'AnonymousAuthenticationModule', 'RequestFilteringModule', 'CustomErrorModule', 'HttpLoggingModule',
            'TracingModule', 'FailedRequestsTracingModule', 'RequestMonitorModule', 'WebSocketModule', 'ConfigurationValidationModule',
            'IpRestrictionModule', 'ModSecurity IIS (32bits)', 'ModSecurity IIS (64bits)', 'ApplicationRequestRouting', 'ARRHelper',
            'DynamicIpRestrictionModule', 'ProcessMonitoringModule', 'ApplicationIdentificationModule', 'WebSocketMonitoringModule',
            'DWASModule', 'AzureSlaModule', 'EasyAuthModule_64bit', 'EasyAuthModule_32bit'
        ];

        var iisEvents = ['URL_CACHE_ACCESS', 'FILE_CACHE_ACCESS'];

        var networkEvents = ['GENERAL_FLUSH_RESPONSE', 'GENERAL_REQUEST_ENTITY'];

        if (iisModules.indexOf(moduleName) >= 0 || iisEvents.indexOf(moduleName) >= 0) {
            return "platform";
        }
        else if (moduleName === "UNKNOWN") {
            return "unknown";
        }
        else if (networkEvents.indexOf(moduleName) >= 0) {
            return "network";
        }
        else {
            return "application";
        }
    };

    $scope.GetIssueIcon = function (typeName) {

        if (typeName === 'platform') {
            return "images/platform.png";
        }
        else if (typeName === 'application') {
            return "images/application.png";
        }
        else if (typeName === 'network') {
            return "images/network.png";
        }
        else if (typeName === 'unknown') {
            return "images/unknown.png";
        }
    };

    $scope.individualRequestController = function () {

        var request = $scope.activeRequest;
        var justMyCode = !document.getElementById("cbJustMyCode").checked;
        var isAsync = !document.getElementById("threadView").checked;

        if ((request.HasThreadStack || request.HasActivityStack) && !request.slowestPipelineEvent.Name.toLowerCase().startsWith(('AspNetCoreModule').toLowerCase())) {
            var asyncFileName = "";
            if (isAsync) {
                asyncFileName = "-async";
            }

            var url = justMyCode ? "reportdata/" + request.ContextId + asyncFileName + "-jmc.json" : "reportdata/" + request.ContextId + asyncFileName + ".json";
            $("#detailsText").css("display", "none");
            $("#stackDetailsPanel").css("display", "");

            $http.get(url).then(function (response) {

                var transformedJson = transFormJson(response.data, 0);
                $('#stackViewerTreeDiv').jstree("destroy");

                var stackTraceTree = $('#stackViewerTreeDiv').jstree({
                    plugins: ["grid"],
                    grid: {
                        columns: [
                            { minWidth: 1000, header: "StackTrace" },
                            { header: "TimeDuration", value: "TimeSpent" }
                        ],
                        resizable: true
                    },
                    'core': {
                        "themes": {
                            "variant": "small",
                            "icons": false
                        },
                        'data': function (obj, callback) {
                            callback.call(this, transformedJson);
                        }
                    }
                });

            });

            if (request.slowestPipelineEvent.EndThreadId === 0) {

                // Check if this is a known pipeline event
                if (knownIssues.indexOf(request.slowestPipelineEvent.Name) >= 0) {
                    $("#detailsText").css("display", "block");
                    $("#stackDetailsPanel").css("display", "none");
                    $("#detailsTemplate").load("solutionmap/" + request.slowestPipelineEvent.Name + ".html");
                }
                else {
                    $("#endThreadIdMessage").css("display", "block");
                    $("#endThreadIdMessage").load("solutionmap/EndThreadIdZero.html");
                }
            }
            else {
                $("#endThreadIdMessage").css("display", "none");
            }
        }
        else {

            $("#detailsText").css("display", "block");
            $("#stackDetailsPanel").css("display", "none");

            // Look for the known modules that can cause slowness
            if (knownIssues.indexOf(request.slowestPipelineEvent.Name) >= 0) {

                if (request.slowestPipelineEvent.Name.toLowerCase().startsWith(('AspNetCoreModule').toLowerCase())) {
                    var tracesExistForCoreRequests = false;
                    if ($scope.corerequests && $scope.corerequests.length > 0) {
                        tracesExistForCoreRequests = true;
                    }
                    if (tracesExistForCoreRequests) {
                        $("#detailsTemplate").load("solutionmap/ReviewAspNetCoreRequestsSection.html");
                    }
                    else {
                        $("#detailsTemplate").load("solutionmap/" + request.slowestPipelineEvent.Name + ".html");
                    }
                }
                else {
                    $("#detailsTemplate").load("solutionmap/" + request.slowestPipelineEvent.Name + ".html");
                }
            }
            else {
                $("#detailsTemplate").load("solutionmap/AsynRequest.html");
            }
        }

    };

    $scope.individualCoreRequestController = function () {

        $scope.lastValue = 0;
        var request = $scope.activeCoreRequest;
        var justMyCode = !document.getElementById("cbJustMyCodeCore").checked;
        var isAsync = true;

        if (request.HasActivityStack) {
            var asyncFileName = "";
            if (isAsync) {
                asyncFileName = "-async";
            }

            var url = justMyCode ? "reportdata/" + request.ActivityId + asyncFileName + "-jmc.json" : "reportdata/" + request.ActivityId + asyncFileName + ".json";

            $("#stackDetailsPanelCore").css("display", "");

            $http.get(url).then(function (response) {

                var transformedJson = transFormJson(response.data, 0);
                $('#stackViewerTreeDivCore').jstree("destroy");

                var stackTraceTree = $('#stackViewerTreeDivCore').jstree({
                    plugins: ["grid"],
                    grid: {
                        columns: [
                            { minWidth: 1000, header: "StackTrace" },
                            { header: "TimeDuration", value: "TimeSpent" }
                        ],
                        resizable: true
                    },
                    'core': {
                        "themes": {
                            "variant": "small",
                            "icons": false
                        },
                        'data': function (obj, callback) {
                            callback.call(this, transformedJson);
                        }
                    }
                });

            });

            url = "reportdata/" + request.ActivityId + "-detailed.json";
            $http.get(url).then(function (response) {
                $scope.detailsCoreRequest = response.data;

            });
        }
        else {
            $("#stackDetailsPanelCore").css("display", "none");
        }

    };

    $scope.UpdateRequest = function (request) {
        $scope.activeRequest = request;

        document.getElementById("cbJustMyCode").checked = false;
        var justMyCode = document.getElementById("cbJustMyCode").checked;

        if (request.HasActivityStack) {
            document.getElementById("syncronous").style.display = 'none';
            document.getElementById("threadView").checked = false;
            document.getElementById("activityView").checked = true;
        }
        if (request.HasThreadStack) {
            document.getElementById("syncronous").style.display = 'inline';
            document.getElementById("threadView").checked = true;
            document.getElementById("activityView").checked = false;
        }

        $scope.individualRequestController();

        $('#requestBar').css("display", "block");

        window.scrollTo(0, document.body.scrollHeight);

    };

    $scope.UpdateCoreRequest = function (request) {
        $scope.activeCoreRequest = request;

        document.getElementById("cbJustMyCodeCore").checked = false;
        var justMyCode = document.getElementById("cbJustMyCodeCore").checked;

        if (request.HasActivityStack) {
            document.getElementById("activityView").checked = true;
        }

        $scope.individualCoreRequestController();
        window.scrollTo(0, document.body.scrollHeight);

    };

    $scope.UpdateCoreFailedRequest = function (request) {
        $('#coreFailedRequestsPanelCore').css("display", "");
        $scope.activeCoreFailedRequest = request;
        let failedRequestUrl = "reportdata/" + request.ActivityId + "-failed-detailed.json";
        $http.get(failedRequestUrl).then(function (response) {
            $scope.detailsCoreFailedRequest = response.data;
        });
    };

    $scope.UpdateFailedRequest = function (failedrequest) {

        $scope.activeFailedRequest = failedrequest;

        var exceptions = new Array();

        for (var e in failedrequest.FailureDetails.ExceptionDetails) {
            var exFound = false;
            var exception = {};
            exception.ExceptionType = failedrequest.FailureDetails.ExceptionDetails[e].ExceptionType + ": " + failedrequest.FailureDetails.ExceptionDetails[e].ExceptionMessage;
            exception.StackTrace = failedrequest.FailureDetails.ExceptionDetails[e].StackTrace;

            for (var i = 0; i < exceptions.length; i++) {
                if (exceptions[i].ExceptionType === exception.ExceptionType && exceptions[i].StackTrace.join() === exception.StackTrace.join()) {
                    exceptions[i].ExceptionCount = exceptions[i].ExceptionCount + 1;
                    exFound = true;
                    break;
                }
            }

            if (!exFound) {
                exception.ExceptionCount = 1;
                exceptions.push(exception);
            }

        }

        var exDisplay = "";
        for (var i = 0; i < exceptions.length; i++) {
            exDisplay += "<br/> (" + exceptions[i].ExceptionCount + ") exception(s) of type <b><span style='color:red'>" + exceptions[i].ExceptionType.replace(/</g, "&lt;").replace(/>/g, "&gt;") + "</span></b><br/>";
            exDisplay += " at " + exceptions[i].StackTrace.join('<br/> at ') + '<br />';
        }

        $('#failedrequestBar').css("display", "block");
        if (exceptions.length > 0) {

            $('#requestExceptionDetails').css("display", "block");
            $('#exceptionDetails').html(exDisplay);
        }
        else {

            if ($scope.activeFailedRequest.FailureDetails.ConfigExceptionInfo) {
                $('#requestExceptionDetails').css("display", "block");
                $('#exceptionDetails').text($scope.activeFailedRequest.FailureDetails.ConfigExceptionInfo);
            }
            else {

                $('#requestExceptionDetails').css("display", "none");
            }
        }

    };

    $scope.simplifyStack = function (stackFrame) {
        var retFrame = stackFrame;
        var brackIndex = stackFrame.indexOf('(');
        if (brackIndex > 0) {
            retFrame = stackFrame.substring(0, brackIndex);
        }
        return retFrame;
    }
});