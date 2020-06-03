// ----- Global Variables ----- //
var schedulerResetInterval = null;

var triggerBladeInitialized = false;
var diagnosersBladeInitialized = false;
var instancesBladeInitialized = false;
var actionsBladeInitialized = false;
var storageBladeInitialized = false;

var repopulateNeeded = false;
var refreshing = false;

var submitting = false;

var exiting = false;

var currentSessionId = null;

var currentSessionReportsDownloaded = false;
var currentReportUrl = "";

var statusenum = Object.freeze({ NotRequested: 0, WaitingForInputs: 1, InProgress: 2, Error: 3, Cancelled: 4, Complete: 5 });

var blobstorageconfigured = 0;

// ----- Common Functions ----- //

function GetBlobInfo() {
    $.ajax({
        type: "GET",
        url: "/DaaS/api/blobinfo",
        dataType: "json",
        async: true,
        success: function (text) {
            if (text == "NotConfigured") {
                blobstorageconfigured = -1;
            }
            else if(text != "Error") {
                blobstorageconfigured = 1;
            }
        }
    });
}

function GetStatusClass(status, type) {
    var classname = "";

    switch (status) {
        case statusenum.NotRequested:
            classname = "status-notrequested-icon";
            break;
        case statusenum.WaitingForInputs:
            classname = "status-waiting-icon";
            break;
        case statusenum.InProgress:
            classname = "status-running-icon";
            break;
        case statusenum.Complete:
            classname = (type == "collector") ? "log-icon" : "report-icon";
            break;
        case statusenum.Error:
            classname = "status-error-icon";
            break;
        case statusenum.Cancelled:
            classname = "status-aborted-icon";
            break;
        default:
            classname = "status-unknown-icon";
            break;
    }

    return classname;
}

function GetSettings(complete) {
    $.ajax({
        type: "GET",
        url: "/DaaS/api/settings",
        dataType: "json",
        async: true,
        error: function () {
            complete("ERROR");
        },
        success: function (text) {
            if (text == "") {
                complete("ERROR");
            }
            else {
                complete(text);
            }
        }
    });
}

function GetDiagnosers(complete) {
    var response = '';
    $.ajax({
        type: "GET",
        url: "/DaaS/api/diagnosers",
        dataType: "json",
        async: true,
        error: function () {
            complete("ERROR");
        },
        success: function (text) {
            if (text == "") {
                complete("ERROR");
            }
            else {
                complete(text);
            }
        }
    });
}

function GetInstances(complete) {
    var response = '';
    $.ajax({
        type: "GET",
        url: "/DaaS/api/instances",
        dataType: "json",
        async: true,
        error: function () {
            complete("ERROR");
        },
        success: function (text) {
            if (text == "") {
                complete("ERROR");
            }
            else {
                complete(text);
            }
        }
    });
}

function GetAllSessions(complete, detailed) {
    if (detailed === undefined || detailed == null) {
        detailed = true;
    }

    var apiUrl = (detailed == true) ? "/DaaS/api/sessions/all/true" : "/DaaS/api/sessions/all";

    var response = '';
    $.ajax({
        type: "GET",
        url: apiUrl,
        dataType: "json",
        async: true,
        error: function () {
            complete("ERROR");
        },
        success: function (text) {
            if (text == null) {
                complete("ERROR");
            }
            else {
                complete(text);
            }
        }
    });
}

function GetRunningSessions(complete, detailed) {
    if (detailed === undefined || detailed == null) {
        detailed = true;
    }

    var apiUrl = (detailed == true) ? "/DaaS/api/sessions/pending/true" : "/DaaS/api/sessions/pending";

    var response = '';
    $.ajax({
        type: "GET",
        url: apiUrl,
        dataType: "json",
        async: true,
        error: function () {
            complete("ERROR");
        },
        success: function (text) {
            if (text == null) {
                complete("ERROR");
            }
            else {
                complete(text);
            }
        }
    });
}

function GetSession(sessionId, detailed) {
    if (detailed === undefined || detailed == null) {
        detailed = true;
    }

    var apiUrl = (detailed == true) ? "/DaaS/api/session/" + sessionId + "/true" : "/DaaS/api/session/" + sessionId;

    var response = '';
    $.ajax({
        type: "GET",
        url: apiUrl,
        dataType: "json",
        async: true,
        error: function () {
            complete("ERROR");
        },
        success: function (text) {
            if (text == "") {
                complete("ERROR");
            }
            else {
                complete(text);
            }
        }
    });
}


// ----- Reset Scheduler Wizard ----- //

function ResetBladeLayering() {
    $('#wizard-trigger-blade').css('z-index', 2220);
    $('#wizard-diagnosers-blade').css('z-index', 2219);
    $('#wizard-instances-blade').css('z-index', 2218);
    $('#wizard-actions-blade').css('z-index', 2217);
    $('#wizard-storage-blade').css('z-index', 2216);
}

function ResetTriggerPage(settings) {
    var time = settings["TimeSpan"].split(":");

    $(".trigger-info-box").hide();

    $("#wizard-trigger-list").val("Live");

    $("#live-trigger-inputs").css("display", "block");
    $("#live-trigger-timespan-hours").val(time[0]);
    $("#live-trigger-timespan-minutes").val(time[1]);
    $("#live-trigger-timespan-seconds").val(time[2]);
    $("#live-trigger-timespan-error").hide();
    $("#trigger-info-box-live").show();

    $("#scheduled-trigger-inputs").css("display", "none");
    $("#scheduled-trigger-startdate").val("");
    $("#scheduled-trigger-startdate-error").hide();
    $("#scheduled-trigger-starttime").val("00:00");
    $("#scheduled-trigger-starttime-error").hide();
    $("#scheduled-trigger-timespan-hours").val(time[0]);
    $("#scheduled-trigger-timespan-minutes").val(time[1]);
    $("#scheduled-trigger-timespan-seconds").val(time[2]);
    $("#scheduled-trigger-timespan-error").hide();

    $("#conditional-trigger-inputs").css("display", "none");
    $("#conditional-trigger-timespan-hours").val(time[0]);
    $("#conditional-trigger-timespan-minutes").val(time[1]);
    $("#conditional-trigger-timespan-seconds").val(time[2]);
    $("#conditional-trigger-timespan-error").hide();
}

function ResetDiagnosersPage(settings) {
    $('.diagnoser-info-box').hide();

    $('#wizard-diagnosers-checklist').find('.checklist-item-input').prop('checked', false);
    for (var i = 0; i < settings["Diagnosers"].length; i++) {
        var targetCheckbox = $("#" + settings["Diagnosers"][i].replace(/ /g, '') + "-diagnoser-option");
        if (targetCheckbox !== undefined && targetCheckbox != null && targetCheckbox.prop('disabled') != true) {
            $(targetCheckbox).prop('checked', true);
        }
    }

    $('#wizard-diagnosers-box-error').hide();

    var checkedDiagnosers = $('#wizard-diagnosers-box').find('input:checkbox:checked');

    if ($(checkedDiagnosers).length != 0) {
        var firstChecked = $(checkedDiagnosers)[0];
        var diagnoserName = $(firstChecked).val().replace(/ /g, '');
        $('#diagnoser-info-box-' + diagnoserName).show();
    }
    else {
        var allDiagnosers = $('#wizard-diagnosers-box').find('input:checkbox');
        if ($(allDiagnosers).length != 0) {
            var firstBox = $(allDiagnosers)[0];
            var diagnoserName = $(firstBox).val().replace(/ /g, '');
            $('#diagnoser-info-box-' + diagnoserName).show();
        }
    }
}

function ResetInstancesPage(instances) {
    if (instances === undefined || instances == null) {
        $('#wizard-trigger-blade-shield-message').text('Retrieving Instances...');
        $('#wizard-trigger-blade-shield').show();
        $('#wizard-trigger-blade-shield-message').show();
        GetInstances(ResetInstancesPage);
    }
    else {
        if (instances == "ERROR") {
            $('#wizard-trigger-blade-shield-message').text('Error Retrieving Instances!');
            $('#wizard-trigger-blade-shield-button').removeClass();
            $('#wizard-trigger-blade-shield-button').addClass("instancesfailure");
            $('#wizard-trigger-blade-shield-button').show();
        }
        else {
            $('#wizard-trigger-blade-shield').hide();
            $('#wizard-trigger-blade-shield-message').hide();
            $('.instances-info-box').hide();

            $('#wizard-instances-checklist').find('.checklist-item-input').prop('checked', true);
            $('#instances-info-box-normal').show();
            $('#wizard-instances-box-shield').hide();

            //Populate instances list
            $("#wizard-instances-checklist").empty();

            for (var i = 0; i < instances.length; i++) {
                var instance = jQuery('<div/>', {
                    "class": "checklist-item"
                });

                var instancediv = jQuery('<div/>', {
                    "class": "checklist-item-container"
                });

                jQuery('<span/>', {
                    "class": "checklist-item-label",
                    text: instances[i],
                    title: instances[i]
                }).appendTo(instancediv);

                jQuery('<input/>', {
                    "class": "checklist-item-input",
                    id: instances[i].replace(/ /g, '') + "-instance-option",
                    type: "checkbox",
                    value: instances[i],
                    name: "instances-selection"
                }).appendTo(instancediv);

                $(instance).append(instancediv);

                $("#wizard-instances-checklist").append(instance);
            }
        }
    }
}

function ResetActionsPage() {
    $('.action-info-box').hide();

    $('#action-troubleshoot').prop('checked', true);
    $('#action-info-box-troubleshoot').show();
    $('#action-restart').hide();
    $('#action-restart').prop('checked', false);
    $('#action-restart-label').hide();
}

function ResetStoragePage(settings) {
    $('.storage-info-box').hide();

    //TODO: hide the page
    $('#storage-current-sasuri').val(settings["BlobSasUri"]);
    $('#wizard-sasurioptions-list').val("Continue Using Current Settings");
    $('#generatesasuri-storage-inputs').hide();
    $("#providesasuri-storage-sasuri").val("");
    $("#providesasuri-storage-sasuri-error").hide();
    $('#providesasuri-storage-inputs').hide();
    $("#generatesasuri-storage-storageaccount").val("");
    $("#generatesasuri-storage-storageaccount-error").hide();
    $("#generatesasuri-storage-storagecontainer").val("");
    $("#generatesasuri-storage-storagecontainer-error").hide();
    $("#generatesasuri-storage-storagekey").val("");
    $("#generatesasuri-storage-storagekey-error").hide();
    $('#storage-info-box-leavecurrentsettings').show();
}

function ResetSchedulerWizard(settings) {

    if (!triggerBladeInitialized || !diagnosersBladeInitialized || !instancesBladeInitialized || !actionsBladeInitialized || !storageBladeInitialized) {
        return;
    }

    if (schedulerResetInterval != null) {
        clearInterval(schedulerResetInterval);
    }

    exiting = false;

    ResetBladeLayering();

    if (settings === undefined || settings == null) {
        $('#wizard-trigger-blade-shield-message').text('Retrieving Settings...');
        $('#wizard-trigger-blade-shield').show();
        $('#wizard-trigger-blade-shield-message').show();
        GetSettings(ResetSchedulerWizard);
    }
    else {
        if (settings == "ERROR") {
            $('#wizard-trigger-blade-shield-message').text('Error Retrieving Settings!');
            $('#wizard-trigger-blade-shield-button').removeClass();
            $('#wizard-trigger-blade-shield-button').addClass("settingsfailure");
            $('#wizard-trigger-blade-shield-button').show();
        }
        else {
            $('#wizard-trigger-blade-shield').hide();
            $('#wizard-trigger-blade-shield-message').hide();

            ResetTriggerPage(settings);
            ResetDiagnosersPage(settings);
            ResetInstancesPage();
            ResetActionsPage();
            ResetStoragePage(settings);
        }
    }
}




// ----- Initialize Scheduler Wizard ----- //

function SetTriggerSelectionChangeHandler() {
    $('#wizard-trigger-list').change(function () {
        $('#live-trigger-inputs').hide();
        $('#scheduled-trigger-inputs').hide();
        $('#conditional-trigger-inputs').hide();
        $('.trigger-info-box').hide();
        $('.instances-info-box').hide();

        var trigger = $(this).val();
        switch (trigger.toLowerCase()) {
            case "live":
                $('#live-trigger-inputs').show();
                $('#trigger-info-box-live').show();
                $('#wizard-instances-box-shield').hide();
                $('#instances-info-box-normal').show();
                $('#action-restart').hide();
                $('#action-restart-label').hide();
                break;
            case "scheduled":
                $('#scheduled-trigger-inputs').show();
                $('#trigger-info-box-scheduled').show();
                $('#wizard-instances-box-shield').show();
                $('#instances-info-box-disabled-scheduled').show();
                $('#action-restart').hide();
                $('#action-restart-label').hide();
                break;
            case "condition based":
                $('#conditional-trigger-inputs').show();
                $('#trigger-info-box-conditional').show();
                $('#wizard-instances-box-shield').show();
                $('#instances-info-box-disabled-conditional').show();
                $('#action-restart').show();
                $('#action-restart-label').show();
                break;
            default:
                $('#live-trigger-inputs').show();
                $('#trigger-info-box-live').show();
                $('#wizard-instances-box-shield').hide();
                $('#instances-info-box-normal').show();
                $('#action-restart').hide();
                $('#action-restart-label').hide();
                break;
        }
    });
}

function SetTriggerPageForwardClickHandler() {
    $('#wizard-trigger-blade').find('.wizard-next-button').click(function () {

        $("#live-trigger-timespan-error").hide();
        $("#scheduled-trigger-startdate-error").hide();
        $("#scheduled-trigger-starttime-error").hide();
        $("#scheduled-trigger-timespan-error").hide();
        $("#conditional-trigger-timespan-error").hide();

        var errorCount = 0;

        var triggerType = $("#wizard-trigger-list").val();
        switch (triggerType.toLowerCase()) {
            case "live":
                var timespanString = $("#live-trigger-timespan-hours").val() + ":" + $("#live-trigger-timespan-minutes").val() + ":" + $("#live-trigger-timespan-seconds").val();
                if (timespanString == "00:00:00") {
                    $("#live-trigger-timespan-error").show();
                    errorCount++;
                }
                break;
            case "scheduled":
                if ($("#scheduled-trigger-startdate").val() == "") {
                    $("#scheduled-trigger-startdate-error").text("*Start Date cannot be empty");
                    $("#scheduled-trigger-startdate-error").show();
                    errorCount++;
                }
                else {
                    var now = new Date();
                    var now_utc = new Date(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), now.getUTCHours(), now.getUTCMinutes(), now.getUTCSeconds()).getTime();

                    var datetimeString = $("#scheduled-trigger-startdate").val() + " " + $("#scheduled-trigger-starttime").val() + ":00";

                    var st = Date.parse(datetimeString);

                    if (st < now_utc) {
                        $("#scheduled-trigger-starttime-error").text("*Start Time cannot be in the past");
                        $("#scheduled-trigger-starttime-error").show();
                        errorCount++;
                    }
                }

                var timespanString = $("#scheduled-trigger-timespan-hours").val() + ":" + $("#scheduled-trigger-timespan-minutes").val() + ":" + $("#scheduled-trigger-timespan-seconds").val();
                if (timespanString == "00:00:00") {
                    $("#scheduled-trigger-timespan-error").show();
                    errorCount++;
                }
                break;
            case "condition based":
                var timespanString = $("#conditional-trigger-timespan-hours").val() + ":" + $("#conditional-trigger-timespan-minutes").val() + ":" + $("#conditional-trigger-timespan-seconds").val();
                if (timespanString == "00:00:00") {
                    $("#conditional-trigger-timespan-error").show();
                    errorCount++;
                }
                break;
            default:
                break;
        }

        if (errorCount != 0) {
            return false;
        }

        return WizardGenericForwardClickHandler($(this));
    });
}

function SetTriggerShieldClickHandler() {
    $('#wizard-trigger-blade-shield-button').click(function () {
        if ($(this).is('.settingsfailure') || $(this).is('.diagnosersfailure') || $(this).is('.instancesfailure')) {
            WizardExitClickHandler();
        }

        $('#wizard-trigger-blade-shield').fadeOut(400);
        $('#wizard-trigger-blade-shield-message').fadeOut(400);
        $('#wizard-trigger-blade-shield-button').fadeOut(400);
        $('#wizard-trigger-blade-shield-button').removeClass();

        return false;
    });
}

function InitializeTriggerPage() {
    SetTriggerSelectionChangeHandler();
    SetTriggerPageForwardClickHandler();
    SetTriggerShieldClickHandler();
    triggerBladeInitialized = true;
}


function SetDiagnosersClickAndSelectionChangeHandler() {
    $('#wizard-diagnosers-checklist').find('.checklist-item-input').change(function () {
        if ($(this).is(':checked')) {
            var diagnoserName = $(this).val().replace(/ /g, '');
            $('.diagnoser-info-box').hide();
            $('#diagnoser-info-box-' + diagnoserName).show();
        }
        return false;
    });

    $('#wizard-diagnosers-checklist').find(".info-button").click(function () {
        var diagnoserName = $(this).parent().find('.checklist-item-input').val().replace(/ /g, '');
        $('.diagnoser-info-box').hide();
        $('#diagnoser-info-box-' + diagnoserName).show();
        return false;
    });

    $('#wizard-diagnosers-checklist').find(".warning-button").click(function () {
        var diagnoserName = $(this).parent().find('.checklist-item-input').val().replace(/ /g, '');
        $('.diagnoser-info-box').hide();
        $('#diagnoser-warning-box-' + diagnoserName).show();
        return false;
    });
}

function SetDiagnosersPageForwardClickHandler() {
    $('#wizard-diagnosers-blade').find('.wizard-next-button').click(function () {

        $('#wizard-diagnosers-box-error').hide();

        var checkedDiagnosers = $('#wizard-diagnosers-box').find('input:checkbox:checked');

        if ($(checkedDiagnosers).length == 0) {
            $('#wizard-diagnosers-box-error').show();
            return false;
        }

        return WizardGenericForwardClickHandler($(this));
    });
}

function InitializeDiagnosersPage(diagnosers) {
    if (diagnosers === undefined || diagnosers == null) {
        $('#wizard-trigger-blade-shield-message').text('Retrieving Diagnosers...');
        $('#wizard-trigger-blade-shield').show();
        $('#wizard-trigger-blade-shield-message').show();
        GetDiagnosers(InitializeDiagnosersPage);
    }
    else {
        if (diagnosers == "ERROR") {
            $('#wizard-trigger-blade-shield-message').text('Error Retrieving Diagnosers!');
            $('#wizard-trigger-blade-shield-button').removeClass();
            $('#wizard-trigger-blade-shield-button').addClass("diagnosersfailure");
            $('#wizard-trigger-blade-shield-button').show();
        }
        else {
            //$('#wizard-trigger-blade-shield').hide();
            //$('#wizard-trigger-blade-shield-message').hide();

            $("#wizard-diagnosers-checklist").empty();
            for (var i = 0; i < diagnosers.length; i++) {
                var diagnoser = jQuery('<div/>', {
                    "class": "checklist-item"
                });

                var diagnoserdiv = jQuery('<div/>', {
                    "class": "checklist-item-container"
                });

                jQuery('<span/>', {
                    "class": "checklist-item-label",
                    text: diagnosers[i]["Name"],
                    title: diagnosers[i]["Name"]
                }).appendTo(diagnoserdiv);


                if (diagnosers[i]["Description"] != null) {

                    var infoButtton = jQuery('<div/>', {
                        "class": "info-button"
                    }).appendTo(diagnoserdiv);

                    var infoText = diagnosers[i]["Description"];

                    var infoBox = jQuery('<div/>', {
                        "class": "diagnoser-info-box",
                        id: "diagnoser-info-box-" + diagnosers[i]["Name"].replace(/ /g, '')
                    })

                    jQuery('<div/>', {
                        "class": "info-box-heading",
                        text: diagnosers[i]["Name"]
                    }).appendTo(infoBox);

                    jQuery('<div/>', {
                        "class": "info-box-strip-help"
                    }).appendTo(infoBox);


                    jQuery('<div/>', {
                        "class": "info-box-text",
                        text: infoText
                    }).appendTo(infoBox);

                    $("#wizard-diagnosers-box-error").after($(infoBox));
                }


                if (diagnosers[i]["Warnings"] != null && diagnosers[i]["Warnings"].length > 0) {

                    jQuery('<div/>', {
                        "class": "warning-button"
                    }).appendTo(diagnoserdiv);

                    var combinedWarningText = "";
                    for (var j = 0; j < diagnosers[i]["Warnings"].length; j++) {
                        combinedWarningText = combinedWarningText + diagnosers[i]["Warnings"][j];
                        if (j != diagnosers[i]["Warnings"].length - 1) {
                            combinedWarningText = combinedWarningText + "<br>";
                        }
                    }

                    var infoBox = jQuery('<div/>', {
                        "class": "diagnoser-info-box",
                        id: "diagnoser-warning-box-" + diagnosers[i]["Name"].replace(/ /g, '')
                    })

                    jQuery('<div/>', {
                        "class": "info-box-heading",
                        text: diagnosers[i]["Name"]
                    }).appendTo(infoBox);

                    jQuery('<div/>', {
                        "class": "info-box-strip-warning"
                    }).appendTo(infoBox);


                    jQuery('<div/>', {
                        "class": "info-box-text",
                        text: combinedWarningText
                    }).appendTo(infoBox);

                    $("#wizard-diagnosers-box-error").after(infoBox);

                    jQuery('<input/>', {
                        "class": "checklist-item-input",
                        id: diagnosers[i]["Name"].replace(/ /g, '') + "-diagnoser-option",
                        type: "checkbox",
                        value: diagnosers[i]["Name"],
                        name: "diagnosers-selection",
                        checked: false,
                        disabled: true
                    }).appendTo(diagnoserdiv);
                }
                else {
                    jQuery('<input/>', {
                        "class": "checklist-item-input",
                        id: diagnosers[i]["Name"].replace(/ /g, '') + "-diagnoser-option",
                        type: "checkbox",
                        value: diagnosers[i]["Name"],
                        disabled: false,
                        name: "diagnosers-selection"
                    }).appendTo(diagnoserdiv);
                }

                $(diagnoser).append(diagnoserdiv);

                $("#wizard-diagnosers-checklist").append(diagnoser);


                SetDiagnosersClickAndSelectionChangeHandler();
            }

            SetDiagnosersPageForwardClickHandler();

            diagnosersBladeInitialized = true;
        }
    }
}


function SetInstancesPageForwardClickHandler() {
    $('#wizard-instances-blade').find('.wizard-next-button').click(function () {
        return WizardGenericForwardClickHandler($(this));
    });
}

function InitializeInstancesPage() {
    SetInstancesPageForwardClickHandler();

    instancesBladeInitialized = true;
}


function SetActionsPageForwardClickHandler() {
    $('#wizard-actions-blade').find('.wizard-next-button').click(function () {
        return WizardGenericForwardClickHandler($(this));
    });
}

function SetActionSelectionChangeHandlers() {
    $("#action-troubleshoot").change(function () {
        $('.action-info-box').hide();
        if ($(this).is(':checked')) {
            $('#action-info-box-troubleshoot').show();
        }
        return false;
    });

    $("#action-collectonly").change(function () {
        $('.action-info-box').hide();
        if ($(this).is(':checked')) {
            $('#action-info-box-collectonly').show();
        }
        return false;
    });

    $("#action-restart").change(function () {
        if ($(this).is(':checked')) {
            $('.action-info-box').hide();
            $('#action-info-box-restart').show();
        }
        return false;
    });
}

function InitializeActionsPage() {
    SetActionSelectionChangeHandlers();
    SetActionsPageForwardClickHandler();
    actionsBladeInitialized = true;
}


function SetStoragePageSubmitClickHandler() {
    $('#wizard-storage-blade').find('.wizard-submit-button').click(function () {

        $("#providesasuri-storage-sasuri-error").hide();
        $("#generatesasuri-storage-storageaccount-error").hide();
        $("#generatesasuri-storage-storagecontainer-error").hide();
        $("#generatesasuri-storage-storagekey-error").hide();

        var errorCount = 0;

        var sasInputOption = $("#wizard-sasurioptions-list").val();
        switch (sasInputOption.toLowerCase()) {
            case "provide sas uri":
                if ($("#providesasuri-storage-sasuri").val() == "") {
                    $("#providesasuri-storage-sasuri-error").show();
                    errorCount++;
                }
                break;
            case "provide account information":
                if ($("#generatesasuri-storage-storageaccount").val() == "") {
                    $("#generatesasuri-storage-storageaccount-error").show();
                    errorCount++;
                }
                if ($("#generatesasuri-storage-storagecontainer").val() == "") {
                    $("#generatesasuri-storage-storagecontainer-error").show();
                    errorCount++;
                }
                if ($("#generatesasuri-storage-storagekey").val() == "") {
                    $("#generatesasuri-storage-storagekey-error").show();
                    errorCount++;
                }
                break;
            default:
                break;
        }

        if (errorCount != 0) {
            return false;
        }


        return WizardSubmitClickHandler();
    });
}

function SetSasUriOptionSelectionChangeHandler() {
    $('#wizard-sasurioptions-list').change(function () {
        $('#providesasuri-storage-inputs').hide();
        $('#generatesasuri-storage-inputs').hide();
        $('.storage-info-box').hide();

        var trigger = $(this).val();
        switch (trigger.toLowerCase()) {
            case "continue using current settings":
                $('#storage-info-box-leavecurrentsettings').show();
                break;
            case "provide sas uri":
                $('#providesasuri-storage-inputs').show();
                $('#storage-info-box-providesasuri').show();
                break;
            case "provide account information":
                $('#generatesasuri-storage-inputs').show();
                $('#storage-info-box-generatesasuri').show();
                break;
            case "do not use blob storage":
                $('#storage-info-box-noblobstorage').show();
                break;
            default:
                $('#storage-info-box-leavecurrentsettings').show();
                break;
        }
    });
}

function SetStorageShieldClickHandler() {
    $('#wizard-storage-blade-shield-button').click(function () {
        if (!$(this).is('.settingsfailure') && !$(this).is('.submitfailure')) {
            WizardExitClickHandler();
        }

        $('#wizard-storage-blade-shield').fadeOut(400);
        $('#wizard-storage-blade-shield-message').fadeOut(400);
        $('#wizard-storage-blade-shield-sub-message').fadeOut(400);
        $('#wizard-storage-blade-shield-button').fadeOut(400);
        $('#wizard-storage-blade-shield-button').removeClass();

        return false;
    });
}

function InitializeStoragePage() {
    SetSasUriOptionSelectionChangeHandler();
    SetStoragePageSubmitClickHandler();
    SetStorageShieldClickHandler();
    storageBladeInitialized = true;
}



function SetWizardLaunchClickHandler() {
    $('#launch-controls-scheduled-diagnose').click(function () {
        //$('#wizard-overlay').fadeToggle(400);
        ConfirmLaunch("custom");
        return false;
    });
}




function WizardGenericForwardClickHandler(target) {
    var grandparent = $(target).parent().parent();
    var grandparentAndSiblings = $(grandparent).parent().children();
    var grandparentIndex = $(grandparentAndSiblings).index(grandparent);
    for (var i = grandparentIndex; i >= 0; i--) {
        var currentZIndex = $(grandparentAndSiblings).eq(i).css("z-index");
        var newZIndex = currentZIndex - 2;
        $(grandparentAndSiblings).eq(i).css("z-index", newZIndex);
    }
    return false;
}


function WizardReverseClickHandler() {
    var grandparent = $(this).parent().parent();
    var grandparentAndSiblings = $(grandparent).parent().children();
    var grandparentIndex = $(grandparentAndSiblings).index(grandparent);
    for (var i = grandparentIndex - 1; i >= 0; i--) {
        var currentZIndex = $(grandparentAndSiblings).eq(i).css("z-index");
        var newZIndex = currentZIndex - (-2);
        $(grandparentAndSiblings).eq(i).css("z-index", newZIndex);
    }
    return false;
}

function SetWizardReverseClickHandler() {
    $('.wizard-prev-button').click(WizardReverseClickHandler);
}


function WizardExitClickHandler() {
    if (!exiting) {
        exiting = true;
        $('#wizard-overlay').fadeToggle(400, ResetSchedulerWizard);
    }
    return false;
}

function SetWizardExitClickHandler() {
    $('.wizard-exit-button').click(WizardExitClickHandler);
}


function WizardSubmitSettings() {
    var NewSettingsInfo = null;

    if ($('#wizard-sasurioptions-list').val().toLowerCase() == "provide sas uri") {
        NewSettingsInfo = {};
        NewSettingsInfo["BlobSasUri"] = $('#providesasuri-storage-sasuri').val();
        NewSettingsInfo["BlobContainer"] = "";
        NewSettingsInfo["BlobKey"] = "";
        NewSettingsInfo["BlobAccount"] = "";
    }
    else if ($('#wizard-sasurioptions-list').val().toLowerCase() == "provide account information") {
        NewSettingsInfo = {};
        NewSettingsInfo["BlobSasUri"] = "";
        NewSettingsInfo["BlobContainer"] = $('#generatesasuri-storage-storagecontainer').val();
        NewSettingsInfo["BlobKey"] = $('#generatesasuri-storage-storagekey').val();
        NewSettingsInfo["BlobAccount"] = $('#generatesasuri-storage-storageaccount').val();
    }
    else if ($('#wizard-sasurioptions-list').val().toLowerCase() == "do not use blob storage") {
        NewSettingsInfo = {};
        NewSettingsInfo["BlobSasUri"] = "clear";
        NewSettingsInfo["BlobContainer"] = "";
        NewSettingsInfo["BlobKey"] = "";
        NewSettingsInfo["BlobAccount"] = "";
    }

    if (NewSettingsInfo != null) {
        NewSettingsInfo["TimeSpan"] = ""; //get from Triggers Blade based on trigger type and whether "save as default" is checked
        NewSettingsInfo["Diagnosers"] = []; //get from Diagnosers Blade if "save as defaults" is checked

        $('#wizard-storage-blade-shield-message').text('Updating Settings...');
        $('#wizard-storage-blade-shield').show();
        $('#wizard-storage-blade-shield-message').show();

        var settingsResult = "";

        $.ajax({
            url: '/DaaS/api/settings',
            async: true,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(NewSettingsInfo),
            processData: false,
            dataType: 'json',
            error: function () {
                $('#wizard-storage-blade-shield-message').text('Update Settings Failed!');
                $('#wizard-storage-blade-shield-sub-message').text('Please verify that the storage settings provided are valid.');
                $('#wizard-storage-blade-shield-sub-message').show();
                $('#wizard-storage-blade-shield-button').removeClass();
                $('#wizard-storage-blade-shield-button').addClass('settingsfailure');
                $('#wizard-storage-blade-shield-button').show();
                submitting = false;
            },
            success: function (text) {
                settingsResult = text;
                if (settingsResult == "" || settingsResult == false) {
                    $('#wizard-storage-blade-shield-message').text('Update Settings Failed!');
                    $('#wizard-storage-blade-shield-sub-message').text('Please verify that the storage settings provided are valid.');
                    $('#wizard-storage-blade-shield-sub-message').show();
                    $('#wizard-storage-blade-shield-button').removeClass();
                    $('#wizard-storage-blade-shield-button').addClass('settingsfailure');
                    $('#wizard-storage-blade-shield-button').show();
                    submitting = false;
                }
                else {
                    repopulateNeeded = true;
                    $('#wizard-storage-blade-shield-message').text('Submitting Analysis...');
                    WizardSubmitAnalysis();
                }
            }
        });
    }
    else {
        $('#wizard-storage-blade-shield-message').text('Submitting Analysis...');
        $('#wizard-storage-blade-shield').show();
        $('#wizard-storage-blade-shield-message').show();
        WizardSubmitAnalysis();
    }
}

function WizardSubmitAnalysis() {
    var StartTime = "";
    var TimeSpan = "";
    var Instances = [];
    var Description = "";

    var CollectLogsOnly = $("#action-collectonly").is(':checked');

    var Diagnosers = [];
    var diagnoserscheckboxes = document.getElementsByName("diagnosers-selection");
    for (var i = 0; i < diagnoserscheckboxes.length; i++) {
        if ($(diagnoserscheckboxes[i]).is(':checked')) {
            Diagnosers.push($(diagnoserscheckboxes[i]).attr("value"));
        }
    }
    //verify at least one diagnoser is selected

    var RunLive = ($("#wizard-trigger-list").val() == "Live");

    if (RunLive) {
        var timespanString = $("#live-trigger-timespan-hours").val() + ":" + $("#live-trigger-timespan-minutes").val() + ":" + $("#live-trigger-timespan-seconds").val();
        TimeSpan = timespanString;
        //verify timespan > 0
        var instancescheckboxes = document.getElementsByName("instances-selection");
        for (var i = 0; i < instancescheckboxes.length; i++) {
            if ($(instancescheckboxes[i]).is(':checked')) {
                Instances.push($(instancescheckboxes[i]).attr("value"));
            }
        }
        //verify at least one instance is selected
    }
    else {
        var datetimeString = $("#scheduled-trigger-startdate").val() + " " + $("#scheduled-trigger-starttime").val() + ":00";
        StartTime = datetimeString;

        var timespanString = $("#scheduled-trigger-timespan-hours").val() + ":" + $("#scheduled-trigger-timespan-minutes").val() + ":" + $("#scheduled-trigger-timespan-seconds").val();
        TimeSpan = timespanString;
        //verify startime >= currenttime
        //verify timespan > 0
    }


    var NewSessionInfo = {};
    NewSessionInfo["RunLive"] = RunLive;
    NewSessionInfo["CollectLogsOnly"] = CollectLogsOnly;
    NewSessionInfo["StartTime"] = StartTime;
    NewSessionInfo["TimeSpan"] = TimeSpan;
    NewSessionInfo["Diagnosers"] = Diagnosers;
    NewSessionInfo["Instances"] = Instances;
    NewSessionInfo["Description"] = Description;

    var sessionId = "";

    $.ajax({
        url: '/DaaS/api/sessions',
        async: true,
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(NewSessionInfo),
        processData: false,
        dataType: 'json',
        error: function (text) {
            $('#wizard-storage-blade-shield-message').text('Submit Analysis Failed! Error: ' + text);
            $('#wizard-storage-blade-shield-button').removeClass();
            $('#wizard-storage-blade-shield-button').addClass('submitfailure');
            $('#wizard-storage-blade-shield-button').show();

            submitting = false;
        },
        success: function (text) {
            sessionId = text;
            if (sessionId == "") {
                $('#wizard-storage-blade-shield-message').text('Submit Analysis Failed! Session was not created');
                $('#wizard-storage-blade-shield-button').removeClass();
                $('#wizard-storage-blade-shield-button').addClass('submitfailure');
                $('#wizard-storage-blade-shield-button').show();
            }
            else {
                $('#wizard-storage-blade-shield-message').text('Analysis Submitted');
                $('#wizard-storage-blade-shield-button').removeClass();
                $('#wizard-storage-blade-shield-button').show();
                RefreshViewer();
            }

            submitting = false;
        }
    });
}

function WizardSubmitClickHandler() {
    if (!submitting) {
        submitting = true;
        WizardSubmitSettings();
    }

    return false;
}


function InitializeSchedulerWizard() {
    triggerBladeInitialized = false;
    diagnosersBladeInitialized = false;
    instancesBladeInitialized = false;
    actionsBladeInitialized = false;
    storageBladeInitialized = false;

    InitializeTriggerPage();
    InitializeDiagnosersPage();
    InitializeInstancesPage();
    InitializeActionsPage();
    InitializeStoragePage();

    SetWizardLaunchClickHandler();
    SetWizardReverseClickHandler();
    SetWizardExitClickHandler();

    schedulerResetInterval = setInterval(ResetSchedulerWizard, 500);
}



// ----- Populating Session View ----- //

function StartCollapsingList(sibling, listclass, complete) {
    var targetlist = null;

    if ($(sibling).next().length > 0) {
        targetlist = $(sibling).next();

        if ($(targetlist).is(listclass)) {
            CollapseList(targetlist.children().first(), complete);
        }
        else if ($(targetlist).is('.session-diagnoser')) {
            complete();
        }
        else if ($(targetlist).next().length > 0) {
            targetlist = $(targetlist).next();

            if ($(targetlist).is(listclass)) {
                CollapseList(targetlist.children().first(), complete);
            }
            else {
                complete();
            }
        }
    }
    else {
        complete();
    }
}

function StartExpandingList(sibling, listclass, complete) {
    var targetlist = null;

    if ($(sibling).next().length > 0) {
        targetlist = $(sibling).next();
        if ($(targetlist).is(listclass)) {
            ExpandList($(targetlist).children().last(), complete, $(targetlist));
        }
        else if ($(targetlist).is('.session-diagnoser')) {
            complete($(targetlist));
        }
        else if ($(targetlist).next().length > 0) {
            targetlist = $(targetlist).next();

            if ($(targetlist).is(listclass)) {

                ExpandList($(targetlist).children().last(), complete, $(targetlist));
            }
            else {
                complete($(targetlist));
            }
        }
        else {
            complete($(targetlist));
        }
    }
    else {
        complete($(targetlist));
    }
}

function CollapseList(element, complete) {
    if ($(element).html() === undefined || $(element) == null) {
        complete();
    }
    else if ($(element).next().length > 0) {
        $(element).slideUp(400);
        setTimeout(function () {
            CollapseList($(element).next(), complete);
        }, 200);
    }
    else {
        $(element).slideUp(400, complete);
    }
}

function ExpandList(element, complete, target) {
    if ($(element).html() === undefined || $(element) == null) {
        complete(target);
    }
    else if ($(element).prev().length > 0) {
        $(element).slideDown(400);
        setTimeout(function () {
            ExpandList($(element).prev(), complete, target);
        }, 200);
    }
    else {
        $(element).slideDown(400, function () {
            complete(target)
        });
    }
}

function DoScrollCorrection(target) {
    var containerHeight = $('#session-container').height();
    var containerCurrentTop = $('#session-container').scrollTop();
    var targetCurrentBottom = $(target).position().top + $(target).height();
    var targetOverhang = targetCurrentBottom - containerHeight;
    if (targetOverhang > 0) {
        var containerTargetTop = containerCurrentTop + targetOverhang;
        $('#session-container').animate({ scrollTop: containerTargetTop }, 400);
    }
}

function SetExpandableListClickHandlers(target) {
    $(target).click(function () {

        var that = $(this);
        var diagnoser = $(this).parent().parent();

        if ($(diagnoser).is(".transitioning")) {
            return false;
        }

        if ($(diagnoser).is(".collapsed")) {
            if ($(this).is(".collectstatus")) {
                $(diagnoser).removeClass("collapsed").addClass("transitioning");
                StartExpandingList(diagnoser, ".expandable-logs", function (target) {
                    $(diagnoser).removeClass("transitioning").addClass("logsexpanded");
                    DoScrollCorrection(target);
                });
            }
            else if ($(this).is(".analyzestatus")) {
                $(diagnoser).removeClass("collapsed").addClass("transitioning");
                StartExpandingList(diagnoser, ".expandable-reports", function (target) {
                    $(diagnoser).removeClass("transitioning").addClass("reportsexpanded");
                    DoScrollCorrection(target);
                });
            }
        }
        else {
            if ($(diagnoser).is(".logsexpanded")) {
                if ($(this).is(".collectstatus")) {
                    $(diagnoser).removeClass("logsexpanded").addClass("transitioning");
                    StartCollapsingList(diagnoser, ".expandable-logs", function (target) {
                        $(diagnoser).removeClass("transitioning").addClass("collapsed");
                    });
                }
                else if ($(this).is(".analyzestatus")) {
                    $(diagnoser).removeClass("logsexpanded").addClass("transitioning");
                    StartCollapsingList(diagnoser, ".expandable-logs", function () {
                        StartExpandingList(diagnoser, ".expandable-reports", function (target) {
                            $(diagnoser).removeClass("transitioning").addClass("reportsexpanded");
                            DoScrollCorrection(target);
                        });
                    });
                }
            }
            else if ($(diagnoser).is(".reportsexpanded")) {
                if ($(this).is(".analyzestatus")) {
                    $(diagnoser).removeClass("reportsexpanded").addClass("transitioning");
                    StartCollapsingList(diagnoser, ".expandable-reports", function () {
                        $(diagnoser).removeClass("transitioning").addClass("collapsed");
                    });
                }
                else if ($(this).is(".collectstatus")) {
                    $(diagnoser).removeClass("reportsexpanded").addClass("transitioning");
                    StartCollapsingList(diagnoser, ".expandable-reports", function () {
                        StartExpandingList(diagnoser, ".expandable-logs", function (target) {
                            $(diagnoser).removeClass("transitioning").addClass("logsexpanded");
                            DoScrollCorrection(target);
                        });
                    });
                }
            }
        }

        return false;
    });
}

function SetReportsDownloadButtonClickHandler() {
    $('#reports-download-button').click(function () {
        $('#reports-download-overlay').fadeOut(400);
        $('#reports-download-message').fadeOut(400);
        $('#reports-download-button').fadeOut(400);

        if (!$(this).is('.submitfailure')) {
            $('#reports-download-button').removeClass();
            var win = window.open(currentReportUrl, '_blank');
            currentReportUrl = "";
            win.focus();
        }
        else {
            $('#reports-download-button').removeClass();
        }
    });
}

function OpenReportLink(url) {
    if (blobstorageconfigured == -1 || currentSessionReportsDownloaded == true) {
        var win = window.open(url, '_blank');
        win.focus();
    }
    else {
        $('#reports-download-message').text('Retrieving Report Files...');
        $('#reports-download-overlay').show();
        $('#reports-download-message').show();

        $.ajax({
            type: "POST",
            url: "/DaaS/api/session/" + currentSessionId + "/downloadreports",
            async: true,
            error: function () {
                $('#reports-download-message').text('Error Retrieving Reports!');
                $('#reports-download-button').removeClass();
                $('#reports-download-button').addClass('submitfailure');
                $('#reports-download-button').show();
            },
            success: function (response) {
                if (response == true) {
                    currentSessionReportsDownloaded = true;
                    $('#reports-download-message').text('Reports Retrieved');
                    $('#reports-download-button').removeClass();
                    $('#reports-download-button').show();
                    currentReportUrl = url;
                }
                else {
                    $('#reports-download-message').text('Error Retrieving Reports!');
                    $('#reports-download-button').removeClass();
                    $('#reports-download-button').addClass('submitfailure');
                    $('#reports-download-button').show();
                }
            }
        });
    }
}

function GenerateExpandableList(elementArray, type) {
    var listContainer = jQuery('<div/>', {
        "class": "expandable-" + type.toLowerCase() + "s"
    });

    for (var i = 0; i < elementArray.length; i++) {
        var element = jQuery('<div/>', {
            "class": type
        });


        var numLeadingColumns = (type.toLowerCase() == "log") ? 1 : 2;
        for (var j = 0; j < numLeadingColumns; j++) {
            jQuery('<div/>', {
                "class": "column-third"
            }).appendTo(element);
        }

        var lastColumn = jQuery('<div/>', {
            "class": "column-third"
        });

        var link;

        if (type.toLowerCase() == "log") {
            var hrefString = "../vfs/data/DaaS/" + elementArray[i]["RelativePath"];

            if (blobstorageconfigured == 1) {
                hrefString = elementArray[i]["FullPermanentStoragePath"];
            }

            link = jQuery('<a/>', {
                href: hrefString,
                text: elementArray[i]["FileName"],
                title: elementArray[i]["FileName"],
                target: "_blank"
            });
        }
        else {
            var url = window.location.protocol + "//" + window.location.host + "/vfs/data/DaaS/" + elementArray[i]["RelativePath"];

            link = jQuery('<a/>', {
                href: "#",
                text: elementArray[i]["FileName"],
                title: url
            }).click(function () {
                var thisUrl = $(this).attr('title');
                OpenReportLink(thisUrl);
                return false;
            });
        }

        $(lastColumn).append(link);

        $(element).append(lastColumn);

        $(listContainer).append(element);
    }

    return listContainer;
}

function GenerateDiagnoserWide(diagSessionObject) {
    var diagnoser = jQuery('<div/>', {
        "class": "session-diagnoser"
    });

    diagnoser.addClass("session-diagnoser-" + diagSessionObject["Name"].replace(/ /g, ''));

    $(diagnoser).addClass("collapsed");

    jQuery('<div/>', {
        "class": "column-spacer",
    }).appendTo(diagnoser);

    var name = jQuery('<div/>', {
        "class": "column-left",
    });

    jQuery('<span/>', {
        text: diagSessionObject["Name"]
    }).appendTo(name);

    $(diagnoser).append(name);

    var collectorStatusClass = GetStatusClass(diagSessionObject["CollectorStatus"], "collector");
    var analyzerStatusClass = GetStatusClass(diagSessionObject["AnalyzerStatus"], "analyzer");

    if (analyzerStatusClass != "status-error-icon" && analyzerStatusClass != "status-notrequested-icon" && collectorStatusClass == "status-error-icon") {
        analyzerStatusClass = "status-aborted-icon";
    }

    var statusClassArray = new Array(collectorStatusClass, analyzerStatusClass);
    var typeClassArray = new Array("collectstatus", "analyzestatus");

    for (var i = 0; i < statusClassArray.length; i++) {
        var status = jQuery('<div/>', {
            "class": "column-third"
        });

        var icon = jQuery('<div/>', {
            "class": statusClassArray[i]

        });

        $(icon).addClass(typeClassArray[i]);

        if (statusClassArray[i] == "log-icon" || statusClassArray[i] == "report-icon") {
            SetExpandableListClickHandlers(icon);
            $(icon).css('cursor', 'pointer');
        }

        $(status).append(icon);

        $(diagnoser).append(status);
    }

    $(diagnoser).css("display", "block");

    return diagnoser;
}

function PopulateSessionView(sessionId) {
    $('#viewer-title-text').html("Session Loading...");
    $('#session-shield').fadeIn(400);

    var sessionStartTime = sessionId;

    $.ajax({
        type: "GET",
        url: "/DaaS/api/session/" + sessionId + "/true",
        dataType: "json",
        async: true,
        error: function() {
            //TODO: show error
        },
        success: function (text) {
            var response = text;
            $('#session-container').empty();

            if (response != "") {
                var sessionObject = response;

                sessionStartTime = sessionObject["StartTime"];
                $('#viewer-title-text').html("Session: " + sessionStartTime);

                var diagnoserObjects = sessionObject["DiagnoserSessions"];
                for (var i = 0; i < diagnoserObjects.length; i++) {
                    $("#session-container").append(GenerateDiagnoserWide(diagnoserObjects[i]));
                    $("#session-container").append(GenerateExpandableList(diagnoserObjects[i]["Logs"], "log"));
                    $("#session-container").append(GenerateExpandableList(diagnoserObjects[i]["Reports"], "report"));
                }
            }

            $('#session-shield').hide();
        }
    });
}


// ----- Populating Category View ----- //

function ScheduleSessionAnalysis(sessionId) {
    var response = '';
    $.ajax({
        type: "POST",
        url: "/DaaS/api/session/" + sessionId + "/startanalysis",
        async: true,
        error: function () {
            $('#express-launch-message').text('Error Launching Analysis!');
            $('#express-launch-button').removeClass();
            $('#express-launch-button').addClass('submitfailure');
            $('#express-launch-button').show();
        },
        success: function (response) {
            if (response == true) {
                $('#express-launch-message').text('Analysis has been started.');
                $('#express-launch-button').removeClass();
                $('#express-launch-button').show();
                RefreshViewer();
            }
            else {
                $('#express-launch-message').text('Error Launching Analysis!');
                $('#express-launch-button').removeClass();
                $('#express-launch-button').addClass('submitfailure');
                $('#express-launch-button').show();
            }
        }
    });

    if (response == "") {
        return false;
    }
    else {
        return response;
    }
}

function SetSessionAnalyzeClickHandler(target) {
    $(target).click(function () {
        //$('#express-launch-message').text('Launching Analysis...');
        //$('#express-launch-overlay').show();
        //$('#express-launch-message').show();

        //var session = $(this).parent().parent().parent();
        //var sessionId = $(session).attr('id');
        //ScheduleSessionAnalysis(sessionId);
        //return false;

        if($(this).text() == "StartAnalysis") {
            var session = $(this).parent().parent().parent();
            var sessionId = $(session).attr('id');
            ConfirmLaunch(sessionId);
        }
    });
}

function CollapseSession(element, complete) {
    if ($(element).next().length > 0) {
        $(element).next().slideUp(400);
        setTimeout(function () {
            CollapseSession($(element).next(), complete);
        }, 200);
    }
    else {
        setTimeout(complete, 200);
    }
}

function ExpandSession(element, complete) {
    if ($(element).prev().length > 0) {
        $(element).slideDown(400);
        setTimeout(function () {
            ExpandSession($(element).prev(), complete);
        }, 200);
    }
    else {
        setTimeout(complete, 200);
    }
}

function SetSessionExpandClickHandler(target) {
    $(target).click(function () {
        var that = $(this);
        var session = $(this).parent().parent().parent();
        if ($(this).is(".contract-button")) {
            if ($(this).parent().parent().parent().is(".expanded")) {
                $(this).parent().parent().parent().removeClass("expanded");
                $(this).parent().parent().parent().addClass("transitioning");
                CollapseSession($(this).parent().parent().parent().children().first(), function () {
                    $(that).removeClass("contract-button");
                    $(that).addClass("expand-button");
                    $(that).parent().parent().parent().removeClass("transitioning");
                    $(that).parent().parent().parent().addClass("collapsed");
                });
            }
        }
        else if ($(this).is(".expand-button")) {
            if ($(this).parent().parent().parent().is(".collapsed")) {
                $(this).parent().parent().parent().removeClass("collapsed");
                $(this).parent().parent().parent().addClass("transitioning");
                ExpandSession($(this).parent().parent().parent().children().last(), function () {
                    $(that).removeClass("expand-button");
                    $(that).addClass("contract-button");
                    $(that).parent().parent().parent().removeClass("transitioning");
                    $(that).parent().parent().parent().addClass("expanded");

                    var containerHeight = $('#sessions-container').height();
                    var containerCurrentTop = $('#sessions-container').scrollTop();
                    var sessionCurrentBottom = $(session).position().top + $(session).height();
                    var sessionOverhang = sessionCurrentBottom - containerHeight;
                    if (sessionOverhang > 0) {
                        var containerTargetTop = containerCurrentTop + sessionOverhang;
                        $('#sessions-container').animate({ scrollTop: containerTargetTop }, 400);
                    }
                });
            }
        }

        return false;
    });
}

function SetSessionDrilldownHandler(target) {
    $(target).click(function () {
        var sessionId = $(this).parent().parent().attr('id');
        currentSessionId = sessionId;
        currentSessionReportsDownloaded = false;
        PopulateSessionView(sessionId);
        $('#viewer-heading-first-text').html("Diagnoser");
        $("#sessions-container").fadeOut(400, function () {
            $("#session-container").fadeIn(400);
        });
        $('#viewer-back-arrow').toggle(400);
        return false;
    });
}

function GenerateDiagnoserSummary(diagSessionObject) {
    var diagSummary = jQuery('<div/>', {
        "class": "session-diagnoser"
    });

    diagSummary.addClass("session-diagnoser-" + diagSessionObject["Name"].replace(/ /g, ''));

    jQuery('<div/>', {
        "class": "column-spacer"
    }).appendTo(diagSummary);

    var name = jQuery('<div/>', {
        "class": "column-left"
    });

    jQuery('<span/>', {
        text: diagSessionObject["Name"]
    }).appendTo(name);

    $(diagSummary).append(name);

    var collectorStatusClass = GetStatusClass(diagSessionObject["CollectorStatus"], "collector");
    var analyzerStatusClass = GetStatusClass(diagSessionObject["AnalyzerStatus"], "analyzer");

    if (analyzerStatusClass != "status-error-icon" && analyzerStatusClass != "status-notrequested-icon" && collectorStatusClass == "status-error-icon") {
        analyzerStatusClass = "status-aborted-icon";
    }

    var classArray = new Array(collectorStatusClass, analyzerStatusClass);

    for (var i = 0; i < classArray.length; i++) {
        var status = jQuery('<div/>', {
            "class": "column-third"
        });

        jQuery('<div/>', {
            "class": classArray[i]
        }).appendTo(status);

        $(diagSummary).append(status);
    }

    return diagSummary;
}

function GenerateSessionSummary(collectStatus, analyzeStatus, startTime, endTime, expanded) {
    if (expanded === undefined || expanded == null) {
        expanded = false;
    }

    var sessionSummary = jQuery('<div/>', {
        "class": "session-summary"
    });

    var spacer = jQuery('<div/>', {
        "class": "column-spacer"
    });

    var expandiconClass = expanded ? "contract-button" : "expand-button";

    var expandicon = jQuery('<div/>', {
        "class": expandiconClass
    });

    SetSessionExpandClickHandler(expandicon);

    $(spacer).append(expandicon);

    $(sessionSummary).append(spacer);

    var friendlyCollectStatus = GetFriendlyStatus(collectStatus);
    var friendlyAnalyzeStatus = GetFriendlyStatus(analyzeStatus);

    var collectStatusColor = GetStatusColor(collectStatus);
    var analyzeStatusColor = GetStatusColor(analyzeStatus);

    if (analyzeStatus == statusenum.NotRequested && collectStatus == statusenum.Complete) {
        friendlyAnalyzeStatus = "StartAnalysis";
        analyzeStatusColor = "black";
    }

    if (analyzeStatus != statusenum.Error && analyzeStatus != statusenum.NotRequested && collectStatus == statusenum.Error) {
        friendlyAnalyzeStatus = "Aborted";
        analyzeStatusColor = "lightgray";
    }

    var classArray = new Array("column-left", "column-third", "column-third ");
    var textArray = new Array(startTime, friendlyCollectStatus, friendlyAnalyzeStatus);
    var colorArray = new Array("black", collectStatusColor, analyzeStatusColor);

    for (var i = 0; i < classArray.length; i++) {

        var element = jQuery('<div/>', {
            "class": classArray[i]
        });

        var span = jQuery('<span/>', {
            text: textArray[i]
        });

        $(span).css('color', colorArray[i]);

        if (textArray[i] == "StartAnalysis") {
            $(span).css('cursor', 'pointer');
        }
        else {
            $(span).css('cursor', 'auto');
        }

        SetSessionAnalyzeClickHandler(span);

        $(element).append(span);

        $(sessionSummary).append(element);
    }

    var drilldownbutton = jQuery('<div/>', {
        "class": "session-drilldown-button"
    });

    SetSessionDrilldownHandler(drilldownbutton);

    $(sessionSummary).append(drilldownbutton);

    return sessionSummary;
}

function GetStatusColor(status) {
    var color = "black";

    switch (status) {
        case statusenum.Error:
            color = "salmon";
            break;
        case statusenum.Cancelled:
        case statusenum.NotRequested:
            color = "lightgray";
            break;
        case statusenum.WaitingForInputs:
            color = "darkgray";
            break;
        case statusenum.InProgress:
            color = "lightblue";
            break;
        case statusenum.Complete:
            color = "lightgreen";
            break;
        default:
            color = "lightgray";
            break;
    }

    return color;
}

function GetFriendlyStatus(status) {
    var friendlyStatus = "";

    switch (status) {
        case statusenum.Error:
            friendlyStatus = "Error";
            break;
        case statusenum.NotRequested:
            friendlyStatus = "NotRequested";
            break;
        case statusenum.WaitingForInputs:
            friendlyStatus = "WaitingForInputs";
            break;
        case statusenum.InProgress:
            friendlyStatus = "InProgress";
            break;
        case statusenum.Complete:
            friendlyStatus = "Complete";
            break;
        case statusenum.Cancelled:
            friendlyStatus = "Cancelled";
            break;
        default:
            friendlyStatus = "Unknown";
            break;
    }

    return friendlyStatus;
}

function GetStatusPrecedence(status) {
    var precedence = 6;

    switch (status) {
        case statusenum.Cancelled:
            precedence = 0;
            break;
        case statusenum.Error:
            precedence = 1;
            break;
        case statusenum.WaitingForInputs:
            precedence = 2;
            break;
        case statusenum.InProgress:
            precedence = 3;
            break;
        case statusenum.Complete:
            precedence = 4;
            break;
        case statusenum.NotRequested:
            precedence = 5;
            break;
        default:
            precedence = 6;
            break;
    }

    return precedence;
}

function ComputeAggregateStatus(currentStatus, newStatus) {
    if (currentStatus == null) {
        if (newStatus == null) {
            return -1;
        }
        else {
            return newStatus;
        }
    }
    else {
        if (newStatus == null) {
            return currentStatus;
        }
        else {
            if (currentStatus == newStatus) {
                return currentStatus;
            }
            else
                return GetStatusPrecedence(currentStatus) < GetStatusPrecedence(newStatus) ? currentStatus : newStatus;
        }
    }
}

function GenerateSession(sessionObject, expanded) {
    if (expanded === undefined || expanded == null) {
        expanded = false;
    }

    var sessionClass = expanded ? "session expanded" : "session collapsed";

    var session = jQuery('<div/>', {
        "class": sessionClass
    });

    $(session).attr("id", sessionObject["SessionId"]);

    var collectStatus = "Unknown";
    var analyzeStatus = "Unknown";

    var diagSessionObjects = sessionObject["DiagnoserSessions"];

    for (var i = 0; i < diagSessionObjects.length; i++) {
        collectStatus = ComputeAggregateStatus(collectStatus, diagSessionObjects[i]["CollectorStatus"]);
        analyzeStatus = ComputeAggregateStatus(analyzeStatus, diagSessionObjects[i]["AnalyzerStatus"]);
        $(session).append(GenerateDiagnoserSummary(diagSessionObjects[i]));
    }

    $(session).prepend(GenerateSessionSummary(collectStatus, analyzeStatus, sessionObject["StartTime"], sessionObject["EndTime"], expanded));

    if (expanded) {
        $(session).children().show();
    }

    return session;
}

function PopulateSessionsView(sessionObjects) {
    if (sessionObjects === undefined || sessionObjects == null)
    {
        $('#sessions-shield').fadeIn(400);
        GetAllSessions(PopulateSessionsView);
    }
    else {
        if (sessionObjects == "ERROR")
        {
            //TODO: Show error
        }
        else if (sessionObjects != "") {
            $("#sessions-container").empty();

            var expanded = true;
            for (var i = 0; i < sessionObjects.length; i++) {
                $("#sessions-container").append(GenerateSession(sessionObjects[i], expanded));
                expanded = false;
            }
        }
        else {
            $("#sessions-container").empty();
        }

        $('#sessions-shield').hide();
    }
}


// ----- Updating/Refreshing ----- //

function RefreshExpandableList(diagnoserNode, elementArray, type) {
    if ($(diagnoserNode).is(".transitioning")) {
        return false;
    }

    var selectorClass = ".expandable-" + type.toLowerCase() + "s";
    var listContainer = $(diagnoserNode).next();
    if (!$(listContainer).is(selectorClass)) {
        listContainer = $(listContainer).next();
        if (!$(listContainer).is(selectorClass)) {
            return false;
        }
    }

    for (var i = 0; i < elementArray.length; i++) {
        var existingElement = $(listContainer).find("a:contains('" + elementArray[i]["FileName"] + "')");

        if (existingElement.length == 0) {
            var element = jQuery('<div/>', {
                "class": type
            });

            var numLeadingColumns = (type.toLowerCase() == "log") ? 1 : 2;
            for (var j = 0; j < numLeadingColumns; j++) {
                jQuery('<div/>', {
                    "class": "column-third"
                }).appendTo(element);
            }

            var lastColumn = jQuery('<div/>', {
                "class": "column-third"
            });

            var link;

            if (type.toLowerCase() == "log") {
                var hrefString = "../vfs/data/DaaS/" + elementArray[i]["RelativePath"];

                if (blobstorageconfigured == 1) {
                    hrefString = elementArray[i]["FullPermanentStoragePath"];
                }

                link = jQuery('<a/>', {
                    href: hrefString,
                    text: elementArray[i]["FileName"],
                    title: elementArray[i]["FileName"],
                    target: "_blank"
                });
            }
            else {
                var url = window.location.protocol + "//" + window.location.host + "/vfs/data/DaaS/" + elementArray[i]["RelativePath"];

                link = jQuery('<a/>', {
                    href: "#",
                    text: elementArray[i]["FileName"],
                    title: url
                }).click(function () {
                    var thisUrl = $(this).attr('title');
                    OpenReportLink(thisUrl);
                    return false;
                });
            }

            $(lastColumn).append(link);

            $(element).append(lastColumn);

            $(listContainer).append(element);
        }
    }

    return listContainer;
}

function PushMessages(diagnoserNode, diagSessionObject, statusMessageDivId, diagnoserType) {
    if (diagSessionObject[diagnoserType] != null)
    {
        var statusMessages = diagSessionObject[diagnoserType];
        var statusMessageDiv = null;

        if ($(diagnoserNode).children("#" + statusMessageDivId).length == 0) {
            statusMessageDiv = jQuery("<div/>", {
                "id": statusMessageDivId,
                "style": "min-width:150px;width:80%",
            });
            $(diagnoserNode).append(statusMessageDiv);
        }
        else {
            statusMessageDiv = $(diagnoserNode).children("#" + statusMessageDivId);
        }

        // First create all the EntityType Divs

        $.each(statusMessages, function (i, val) {
            var divStatusPerEntity = null;

            if ($(statusMessageDiv).children("#" + val.EntityType).length == 0) {
                divStatusPerEntity = jQuery("<div/>", {
                    "id": val.EntityType,
                    "style" : "margin-left:30px;margin-top:10px",
                    text: val.EntityType,
                });
               
                $(statusMessageDiv).append(divStatusPerEntity);
            }
            else {
                divStatusPerEntity = $(statusMessageDiv).children("#" + val.EntityType);
            }
            $(divStatusPerEntity).empty();
            $(divStatusPerEntity).text(val.EntityType);
        });

        // loop through all the messages and add them to EntityType divs.
        $.each(statusMessages, function (i, val)
        {
            var divStatusPerEntity = $(statusMessageDiv).children("#" + val.EntityType);
            var divActualMessage = null;
            if ($(divStatusPerEntity).children("#actualMessage").length == 0) {
                divActualMessage = jQuery("<div/>", {
                    "id": "actualMessage",
                    "style" : "overflow-y: auto;height: 100px; margin-left:5px;margin-top:10px"
                });
                $(divStatusPerEntity).append(divActualMessage);
            }
            else {
                divActualMessage = $(divStatusPerEntity).children("#actualMessage");
            }

            var existingText = divActualMessage.html();
            divActualMessage.html(existingText + val.Message + '<br/>');
            //divActualMessage.scrollTop = divActualMessage.scrollHeight;
        });
    }
}

function RefreshDiagnoserWide(diagnoserNode, diagSessionObject) {
    var collectorStatusClass = GetStatusClass(diagSessionObject["CollectorStatus"], "collector");
    var analyzerStatusClass = GetStatusClass(diagSessionObject["AnalyzerStatus"], "analyzer");

    if (analyzerStatusClass != "status-error-icon" && analyzerStatusClass != "status-notrequested-icon" && collectorStatusClass == "status-error-icon") {
        analyzerStatusClass = "status-aborted-icon";
    }

    var statusMessageDivIdCollector = 'statusMessage' + diagSessionObject["Name"].replace(/ /g, '') + "Collector";
    if (diagSessionObject["CollectorStatus"] == statusenum.InProgress) {
        PushMessages(diagnoserNode, diagSessionObject, statusMessageDivIdCollector, "CollectorStatusMessages");
    }
    else
    {        
        if ($(diagnoserNode).children("#" + statusMessageDivIdCollector).length > 0) {
            $(diagnoserNode).children("#" + statusMessageDivIdCollector).remove();
        }
    }

    var statusMessageDivIdAnalyzer = 'statusMessage' + diagSessionObject["Name"].replace(/ /g, '') + "Analyzer";
    if (diagSessionObject["AnalyzerStatus"] == statusenum.InProgress) {

        PushMessages(diagnoserNode, diagSessionObject, statusMessageDivIdAnalyzer, "AnalyzerStatusMessages");
    }
    else {
        if ($(diagnoserNode).children("#" + statusMessageDivIdAnalyzer).length > 0) {
            $(diagnoserNode).children("#" + statusMessageDivIdAnalyzer).remove();
        }
    }

    var statusClassArray = new Array(collectorStatusClass, analyzerStatusClass);
    var typeClassArray = new Array("collectstatus", "analyzestatus");

    for (var i = 0; i < statusClassArray.length; i++) {
        var statusNode = $(diagnoserNode).find("." + typeClassArray[i]);

        if (!$(statusNode).is("." + statusClassArray[i])) {
            $(statusNode).attr('class', typeClassArray[i]);
            $(statusNode).addClass(statusClassArray[i])

            if (statusClassArray[i] == "log-icon" || statusClassArray[i] == "report-icon") {
                SetExpandableListClickHandlers(statusNode);
                $(statusNode).css('cursor', 'pointer');
            }
        }
    }
}

function RefreshSessionInDrilldown(sessionObject) {
    var diagnoserObjects = sessionObject["DiagnoserSessions"];
    for (var i = 0; i < diagnoserObjects.length; i++) {
        var selectorClass = ".session-diagnoser-" + diagnoserObjects[i]["Name"].replace(/ /g, '');
        var diagnoserNode = $('#session-container').find(selectorClass);

        RefreshDiagnoserWide(diagnoserNode, diagnoserObjects[i]);
        RefreshExpandableList(diagnoserNode, diagnoserObjects[i]["Logs"], "log");
        RefreshExpandableList(diagnoserNode, diagnoserObjects[i]["Reports"], "report");
    }
}


function RefreshDiagnoserSummary(sessionNode, diagSessionObject) {
    var selectorClass = ".session-diagnoser-" + diagSessionObject["Name"].replace(/ /g, '');
    var diagSummaryNode = $(sessionNode).find(selectorClass);


    var collectorStatusClass = GetStatusClass(diagSessionObject["CollectorStatus"], "collector");
    var analyzerStatusClass = GetStatusClass(diagSessionObject["AnalyzerStatus"], "analyzer");

    if (analyzerStatusClass != "status-error-icon" && analyzerStatusClass != "status-notrequested-icon" && collectorStatusClass == "status-error-icon") {
        analyzerStatusClass = "status-aborted-icon";
    }

    var classArray = new Array(collectorStatusClass, analyzerStatusClass);

    var statusColumns = $(diagSummaryNode).find(".column-third");

    for (var i = 0; i < $(statusColumns).length; i++) {
        var statusColumn = $(statusColumns)[i];
        var statusColumnChild = $(statusColumn).children()[0];
        if (!$(statusColumnChild).is("." + classArray[i])) {
            $(statusColumnChild).removeClass();
            $(statusColumnChild).addClass(classArray[i]);
        }
    }
}

function RefreshSessionSummary(sessionNode, collectStatus, analyzeStatus) {
    var sessionSummaryNode = $(sessionNode).find('.session-summary');

    var friendlyCollectStatus = GetFriendlyStatus(collectStatus);
    var friendlyAnalyzeStatus = GetFriendlyStatus(analyzeStatus);

    var collectStatusColor = GetStatusColor(collectStatus);
    var analyzeStatusColor = GetStatusColor(analyzeStatus);

    if (analyzeStatus == statusenum.NotRequested && collectStatus == statusenum.Complete) {
        friendlyAnalyzeStatus = "StartAnalysis";
        analyzeStatusColor = "black";
    }

    if (analyzeStatus != statusenum.Error && analyzeStatus != statusenum.NotRequested && collectStatus == statusenum.Error) {
        friendlyAnalyzeStatus = "Aborted";
        analyzeStatusColor = "lightgray";
    }

    var textArray = new Array(friendlyCollectStatus, friendlyAnalyzeStatus);
    var colorArray = new Array(collectStatusColor, analyzeStatusColor)

    var statusColumns = $(sessionSummaryNode).find(".column-third");

    for (var i = 0; i < $(statusColumns).length; i++) {
        var statusColumn = $(statusColumns)[i];
        var statusColumnChild = $(statusColumn).children()[0];

        if (textArray[i] == "StartAnalysis") {
            $(statusColumnChild).css('cursor', 'pointer');
        }
        else {
            $(statusColumnChild).css('cursor', 'auto');
        }

        $(statusColumnChild).text(textArray[i]);
        $(statusColumnChild).css('color', colorArray[i]);
    }
}

function RefreshSessionInList(sessionObject) {
    var sessionNode = $('#' + sessionObject["SessionId"]);

    var collectStatus = "Unknown";
    var analyzeStatus = "Unknown";

    var diagSessionObjects = sessionObject["DiagnoserSessions"];

    for (var i = 0; i < diagSessionObjects.length; i++) {
        collectStatus = ComputeAggregateStatus(collectStatus, diagSessionObjects[i]["CollectorStatus"]);
        analyzeStatus = ComputeAggregateStatus(analyzeStatus, diagSessionObjects[i]["AnalyzerStatus"]);
        RefreshDiagnoserSummary(sessionNode, diagSessionObjects[i]);
    }

    RefreshSessionSummary(sessionNode, collectStatus, analyzeStatus);
}

function RefreshViewer(sessions) {
    if (refreshing) {
        return false;
    }

    if (sessions === undefined || sessions == null) {
        GetAllSessions(RefreshViewer);
    }
    else {
        GetBlobInfo();

        if (repopulateNeeded)
        {
            $("#sessions-container").empty();
            repopulateNeeded = false;
        }

        refreshing = true;
        if (sessions != "ERROR") {
            if (sessions != null && sessions != "") {
                for (var i = sessions.length - 1; i >= 0; i--) {
                    //Refresh the drilldown session view if it matches this session
                    if (currentSessionId != null && currentSessionId == sessions[i]["SessionId"]) {
                        RefreshSessionInDrilldown(sessions[i]);
                    }
                    //If this session is in the sessions display list, refresh it.
                    //Otherwise add it to the top of the list.
                    if ($('#' + sessions[i]["SessionId"]).length > 0) {
                        RefreshSessionInList(sessions[i]);
                    }
                    else {
                        var sessionNode = GenerateSession(sessions[i]);
                        $("#sessions-container").prepend(sessionNode);
                    }
                }
            }
        }
        refreshing = false;
    }
}



function SetSessionBackArrowClickHandler() {
    $('#viewer-back-arrow').click(function () {
        $('#viewer-title-text').html("Sessions");
        $('#viewer-heading-first-text').html("StartTime");
        $("#session-shield").fadeOut(400);
        $("#session-container").fadeOut(400, function () {
            $("#sessions-container").fadeIn(400);
        });
        $('#viewer-back-arrow').toggle(400);
        return false;
    });
}

function SetSchedulerHelpClickHandler() {
    $('.launch-controls-help-open-button,.launch-controls-help-close-button').click(function () {
        $(this).parent().children('.launch-controls-help-text').fadeToggle(200);

        $(this).toggleClass('launch-controls-help-open-button');
        $(this).toggleClass('launch-controls-help-close-button');

        return false;
    });

    $('.launch-controls-help-text').click(function () {
        return false;
    });
}


function SubmitExpressAnalyze(settings) {
    if (settings == "ERROR") {
        $('#express-launch-message').text('Error Retrieving Settings!');
        $('#express-launch-button').removeClass();
        $('#express-launch-button').addClass('submitfailure');
        $('#express-launch-button').show();
    }
    else {

        $('#express-launch-message').text('Submitting Analysis...');

        var NewSessionInfo = {};
        NewSessionInfo["RunLive"] = true;
        NewSessionInfo["CollectLogsOnly"] = false;
        NewSessionInfo["StartTime"] = "";
        NewSessionInfo["TimeSpan"] = settings["TimeSpan"];
        NewSessionInfo["Diagnosers"] = settings["Diagnosers"];
        NewSessionInfo["Instances"] = [];
        NewSessionInfo["Description"] = "";

        var sessionId = "";

        $.ajax({
            url: '/DaaS/api/sessions',
            async: true,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(NewSessionInfo),
            processData: false,
            dataType: 'json',
            success: function (text) {
                sessionId = text;
                if (sessionId == "") {
                    $('#express-launch-message').text('Submit Express Analysis Failed!');
                    $('#express-launch-button').removeClass();
                    $('#express-launch-button').addClass('submitfailure');
                    $('#express-launch-button').show();
                }
                else {
                    $('#express-launch-message').text('Analysis Submitted');
                    $('#express-launch-button').removeClass();
                    $('#express-launch-button').show();
                    RefreshViewer();
                }
            }
        });
    }
}

function ExpressAnalyzeClickHandler() {
    $('#express-launch-message').text('Retrieving Settings...');
    $('#express-launch-overlay').show();
    $('#express-launch-message').show();

    GetSettings(SubmitExpressAnalyze);

    return false;
}

function SetExpressAnalyzeClickHandler() {
    //$('#launch-controls-live-diagnose').click(ExpressAnalyzeClickHandler);
    $('#launch-controls-live-diagnose').click(function () {
        ConfirmExpressLaunch();
    });

    $('#express-launch-button').click(function () {
        $('#express-launch-overlay').hide();
        $('#express-launch-message').hide();
        $('#express-launch-button').hide();
        $('#express-launch-button').removeClass();

        return false;
    });
}

function ConfirmExpressLaunch() {
    $('#express-confirmation-button-continue').show();
    $('#express-confirmation-button-cancel').show();
    $('#express-confirmation-message').text("Start Express Analysis?");
    $('#express-confirmation-overlay').show();
}

function SetConfirmExpressLaunchClickHandlers() {
    $('#express-confirmation-button-continue').click(function () {
        $('#express-confirmation-overlay').hide();
        ConfirmLaunch("express");

        return false;
    });

    $('#express-confirmation-button-cancel').click(function () {
        $('#express-confirmation-overlay').hide();

        return false;
    });
}


function SetConfirmLaunchClickHandlers() {
    $('#confirmation-button-continue').click(function () {
        if ($(this).is('.express')) {
            $('#confirmation-overlay').fadeOut(400);
            ExpressAnalyzeClickHandler();
        }
        else if ($(this).is('.custom')) {
            $('#confirmation-overlay').fadeOut(400);
            $('#wizard-overlay').fadeToggle(400);
        }
        else {
            var sessionId = $(this).attr('class');
            $('#confirmation-overlay').fadeOut(400);
            $('#express-launch-message').text('Launching Analysis...');
            $('#express-launch-overlay').show();
            $('#express-launch-message').show();
            ScheduleSessionAnalysis(sessionId);
        }

        return false;
    });

    $('#confirmation-button-cancel').click(function () {
        if ($(this).is('.express')) {
            $('#confirmation-overlay').hide();
        }
        else if ($(this).is('.custom')) {
            $('#confirmation-overlay').hide();
        }
        else {
            var sessionId = $(this).attr('class');
            $('#confirmation-overlay').hide();
        }

        return false;
    });
}

function ConfirmLaunchHandler(runningsessions) {
    if (runningsessions == "ERROR") {
        $('#confirmation-message').text("Failed to get check for running sessions. Are you sure you want to continue?");
        $('#confirmation-button-continue').show();
        $('#confirmation-button-cancel').show();
    } else if (runningsessions.length > 0) {
        $('#confirmation-message').text("You already have sessions running. Are you sure you want to continue?");
        $('#confirmation-button-continue').show();
        $('#confirmation-button-cancel').show();
    } else {
        if ($('#confirmation-button-continue').is('.express')) {
            $('#confirmation-overlay').fadeOut(400);
            ExpressAnalyzeClickHandler();
        }
        else if ($('#confirmation-button-continue').is('.custom')) {
            $('#confirmation-overlay').fadeOut(400);
            $('#wizard-overlay').fadeToggle(400);
        }
        else {
            var sessionId = $('#confirmation-button-continue').attr('class');
            $('#confirmation-overlay').fadeOut(400);
            $('#express-launch-message').text('Launching Analysis...');
            $('#express-launch-overlay').show();
            $('#express-launch-message').show();
            ScheduleSessionAnalysis(sessionId);
        }
    }
}

function ConfirmLaunch(tag) {
    $('#confirmation-button-continue').removeClass();
    $('#confirmation-button-continue').addClass(tag);
    $('#confirmation-button-continue').hide();
    $('#confirmation-button-cancel').hide();
    $('#confirmation-message').text("Checking for running sessions...");
    $('#confirmation-overlay').show();

    GetRunningSessions(ConfirmLaunchHandler);
}

jQuery(document).ready(function ($) {
    SetSessionExpandClickHandler('.expand-button');
    SetSessionDrilldownHandler('.session-drilldown-button');

    SetSchedulerHelpClickHandler();
    SetSessionBackArrowClickHandler();

    InitializeSchedulerWizard();
    SetExpressAnalyzeClickHandler();

    SetConfirmLaunchClickHandlers();

    SetConfirmExpressLaunchClickHandlers();

    SetReportsDownloadButtonClickHandler();

    GetBlobInfo();

    PopulateSessionsView();

    setInterval(RefreshViewer, 15 * 1000);

    $('#scheduled-trigger-startdate').datepicker();
});