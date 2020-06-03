var knownMicrosoftModulesPrefix = ['microsoft.', 'system.', 'process32 ', 'thread ', 'process64 ', 'process ', 'other ','kernel32!', 'coreclr!','clr!', 'ntdll!', 'ntoskrnl!','hal!','picohelper', 'webengine', 'kernelbase!','wow64','iiscore','w3dt'];
if (!String.prototype.startsWith) {
    String.prototype.startsWith = function (searchString, position) {
        position = position || 0;
        return this.indexOf(searchString, position) === position;
    };
}

function getNotification(input) {
    if (!input) {
        return "N/A";
    }

    var notificationName = "";

    switch (input) {
        case 1:
            notificationName = "BeginRequest";
            break;
        case 2:
            notificationName = "AuthenticateRequest";
            break;
        case 4:
            notificationName = "AuthorizeRequest";
            break;
        case 8:
            notificationName = "ResolveRequestCache";
            break;
        case 16:
            notificationName = "MapRequestHandler";
            break;
        case 32:
            notificationName = "AcquireRequestState";
            break;
        case 64:
            notificationName = "PreExecuteRequestHandler";
            break;
        case 128:
            notificationName = "ExecuteRequestHandler";
            break;
        case 256:
            notificationName = "ReleaseRequestState";
            break;
        case 512:
            notificationName = "UpdateRequestCache";
            break;
        case 1024:
            notificationName = "LogRequest";
            break;
        case 2048:
            notificationName = "EndRequest";
            break;
        case 536870912:
            notificationName = "SendResponse";
            break;

        default:
            notificationName = toString(input);
    }

    return notificationName;
}

function replaceAll(str, find, replace) {
    return str.replace(new RegExp(find, 'g'), replace);
}

function beautifyFunctionName(input) {
    input = replaceAll(input, '<', '&lt;');
    input = replaceAll(input, '>', '&gt;');

    var strReturn = input;

    if (input.indexOf('(') > 0 && input.indexOf('!') > 0 && !input.startsWith('Thread ') && !input.startsWith('Activity ')) {
        strReturn = input.substring(0, input.indexOf('('));
        if (strReturn.length > 150) {
            strReturn = strReturn.substring(0, 150) + "...";
        }
    }
    else {
        if (strReturn.length > 150) {
            strReturn = strReturn.substring(0, 150) + "...";
        }
    }

    for (var i = 0; i < knownMicrosoftModulesPrefix.length; i++) {
        if (strReturn.toLowerCase().startsWith(knownMicrosoftModulesPrefix[i])) {
            strReturn = "<span style='color:darkgray'>" + strReturn + "</span>";
            break;
        }
    }

    return strReturn;
}


function transFormJson(node, rootTimeSpent) {

    if (rootTimeSpent === 0) {
        rootTimeSpent = node.TimeSpent;
    }

    var newNode = {};
    newNode.children = [];
    newNode.text = beautifyFunctionName(node.FunctionName);

    var percentOfTotalTimeSpent = node.TimeSpent / rootTimeSpent;
    if (percentOfTotalTimeSpent > 0.8) {
        newNode.state = {};
        newNode.state.opened = true;
        newNode.text = "<img src='images/hotpath.png' /> &nbsp;&nbsp;" + newNode.text;
    }


    newNode.data = {};
    newNode.data.TimeSpent = Math.round(node.TimeSpent);
    newNode.data.InclusiveMetricPercent = Math.round(node.InclusiveMetricPercent);
    newNode.data.ExclusiveTime = Math.round(node.ExclusiveTime);

    var len = node.childNodes.length;

    for (var k in node.childNodes) {
        var childElement = transFormJson(node.childNodes[k], node.TimeSpent);
        newNode.children.push(childElement);
    }

    return newNode;
}