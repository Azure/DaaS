
// Plotting functions


// plot basic chart
// chardData: data for chart, it has following format: { plotName: [ [xValue, yValue], [xValue, yValue] ... ], plot2Name: [...] }
// hasSeparateYaxis: boolean telling if scaling should be omitted, so every plot will have separate yaxis, all yaxis scales will be hidden on chart
// plotContainseSelector: jQuery selector pointing to location where plot should be shown, e.g. "#plot-3"
// chartChoicesContainerSelector: jQuery selector poiting to location where choices for chart should be placed, e.g. "#choices-3"
// defaultSelectedPlots: list of names of plots that should be shown by default
function plotChart(chartData, hasSeparateYaxis, plotContainerSelector, chartChoicesContainerSelector, defaultSelectedPlots) {
    var datasets = {};
    var i = 0;
    for (var key in chartData) {
        datasets[key] = {
            label: key,
            data: chartData[key]
        };
        if (hasSeparateYaxis) {
            datasets[key].yaxis = ++i;
        }
    }
    
    // hard-code color indices to prevent them from shifting as
    // countries are turned on/off

    var i = 0;
    $.each(datasets, function (key, val) {
        val.color = i;
        ++i;
    });
    
    // insert checkboxes 
    var choiceContainer = $(chartChoicesContainerSelector);
    $.each(datasets, function (key, val) {
        var checked = '';
        if (defaultSelectedPlots.indexOf(key) >= 0) {
            checked = 'checked="checked"';
        }
        choiceContainer.append("<br/><input type='checkbox' name='" + key +
            "' " + checked + " id='id" + key + "'></input>" +
            "<label for='id" + key + "'>"
            + val.label + "</label>");
    });
    
    choiceContainer.find("input").click(plotAccordingToChoices);
    
    function plotAccordingToChoices() {

        var data = [];

        choiceContainer.find("input:checked").each(function () {
            var key = $(this).attr("name");
            if (key && datasets[key]) {
                data.push(datasets[key]);
            }
        });
        var yaxis = { };
        if (hasSeparateYaxis) {
            yaxis.show = false;
        }
        if (data.length > 0) {
            $.plot(plotContainerSelector, data, {
                yaxis: yaxis,
                xaxis: {
                    mode: 'time',
                    timeformat: '%H-%M-%S',
                },
                series: {
                    lines: { show: true },
                    points: { show: false }
                },

               grid: {
                    hoverable: true,
                    clickable: true
                }
            });
            $(plotContainerSelector).bind('plothover', function (event, pos, item) {
                if (item) {
                    var x = item.datapoint[0].toFixed(2),
                        y = item.datapoint[1].toFixed(2);

                    $('#chart-tooltip').html(item.series.label + ' = ' + y)
                        .css({ top: item.pageY + 5, left: item.pageX + 5 })
                        .fadeIn(200);
                } else {
                    $('#chart-tooltip').hide();
                }
            });

        }
    }

    plotAccordingToChoices();
}

