var app = angular.module('app', ['angular.filter']);

app.controller('Ctrl', function Ctrl($scope, $http, $filter) {
    var url = "reportdata/stackDump.json";
    $http.get(url).then(function (response) {
        $scope.stackDump = response.data;
        let states = [...new Set($scope.stackDump.Threads.map(item => item.State))];
        $scope.states = states;

        if ($scope.stackDump.DeadlockMessage) {
            $scope.DeadlockMessage = $scope.stackDump.DeadlockMessage.split('\r\n');
        }               
    });
   
    // https://docs.oracle.com/javase/jp/8/docs/serviceabilityagent/sun/jvm/hotspot/runtime/JavaThreadState.html

    $scope.state = new Object();
    $scope.state['BLOCKED'] = { class: "danger", Icon: "fa-lock", desc: 'Blocked in vm' };
    $scope.state['IN_NATIVE'] = { class: "warning", Icon: "fa-file", desc: 'Running in native code' };
    $scope.state['IN_NATIVE_TRANS '] = { class: "info", Icon: "fa-unlock-alt", desc: 'Corresponding transition state' };
    $scope.state['IN_VM'] = { class: "success", Icon: "fa-unlock", desc: 'Running in VM' };
    $scope.state['IN_VM_TRANS '] = { class: "danger", Icon: "fa-clock-o", desc: 'Corresponding transition state' };
    $scope.state['IN_JAVA'] = { class: "warning", Icon: "fa-pause", desc: 'Running in Java or in stub code' };
    $scope.state['BLOCKED_TRANS'] = { class: "info", Icon: "fa-fire", desc: 'Corresponding transition state' };
    $scope.state['UNINITIALIZED'] = { class: "success", Icon: "fa-ban", desc: 'Should never happen (missing initialization)' };
    $scope.state['NEW'] = { class: "success", Icon: "fa-plus", desc: 'Just starting up, i.e., in process of being initialized' };

    $scope.by_State = '';

    $scope.updateState = function (stateName, $event) {
        $(".statis .box").css("border", "0px");
        $(".statis .box").css("opacity", "0.8");
        $event.currentTarget.style = "border:3px solid green";
        $($event.currentTarget).animate({ opacity: '1.0' }, "slow");
        $scope.by_State = stateName;
    };

    $scope.getSelectedState = function (stateName) {
        return ($scope.by_State == '' ? 'all' : $scope.by_State);

    }

    $scope.simplifyStack = function (stackFrame) {
        var retFrame = stackFrame;
        var brackIndex = stackFrame.indexOf('(');
        if (brackIndex > 0) {
            retFrame = stackFrame.substring(0, brackIndex);
        }
        return retFrame;
    }
   
});

