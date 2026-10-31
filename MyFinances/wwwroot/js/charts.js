// Charts on the dashboard and reports. Each <canvas data-chart> carries
// its own data as JSON; this file only draws it.
//
// Colours are the validated reference palette (categorical slots in a
// fixed order, a separate step for dark mode). Text uses the page's
// ink, never a series colour.

(function () {
    "use strict";

    if (!window.Chart) {
        return;
    }

    var dark = document.documentElement.getAttribute("data-theme") === "dark";

    var palette = dark
        ? ["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#9085e9", "#e66767"]
        : ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948"];

    // Income and spending are two identities, drawn from adjacent slots.
    var income = palette[2];
    var expense = palette[1];
    var other = dark ? "#5b6b85" : "#a3adbb";

    var styles = getComputedStyle(document.documentElement);
    var ink = styles.getPropertyValue("--text-secondary").trim();
    var grid = styles.getPropertyValue("--border").trim();
    var surface = styles.getPropertyValue("--bg-card").trim();

    Chart.defaults.font.family = "Montserrat, system-ui, sans-serif";
    Chart.defaults.font.size = 12;
    Chart.defaults.color = ink;

    // The same signs Money.Symbol uses on the server.
    var symbols = { USD: "$", ZAR: "R", GBP: "£", EUR: "€", ZWG: "ZWG " };

    function money(value, currency) {
        var text = Math.abs(value).toLocaleString("en-GB", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        return (value < 0 ? "-" : "") + (symbols[currency] || currency + " ") + text;
    }

    function compact(value, currency) {
        var abs = Math.abs(value);
        var text = abs >= 1e6 ? (abs / 1e6).toFixed(1) + "m" : abs >= 1e4 ? (abs / 1e3).toFixed(0) + "k" : abs.toFixed(0);
        return (value < 0 ? "-" : "") + (symbols[currency] || currency + " ") + text;
    }

    var builders = {
        flows: function (data) {
            return {
                type: "bar",
                data: {
                    labels: data.labels,
                    datasets: [
                        { label: "Income", data: data.income, backgroundColor: income },
                        { label: "Expenses", data: data.expenses, backgroundColor: expense }
                    ]
                },
                options: {
                    maintainAspectRatio: false,
                    datasets: {
                        bar: {
                            borderRadius: { topLeft: 4, topRight: 4 },
                            borderSkipped: "bottom",
                            borderColor: surface,
                            borderWidth: { left: 1, right: 1 },
                            maxBarThickness: 28,
                            categoryPercentage: 0.7,
                            barPercentage: 0.9
                        }
                    },
                    interaction: { mode: "index", intersect: false },
                    plugins: {
                        legend: { position: "top", align: "end", labels: { boxWidth: 10, boxHeight: 10, useBorderRadius: true, borderRadius: 3 } },
                        tooltip: {
                            callbacks: {
                                label: function (c) { return c.dataset.label + ": " + money(c.parsed.y, data.currency); },
                                footer: function (items) {
                                    var net = (items[0] ? items[0].parsed.y : 0) - (items[1] ? items[1].parsed.y : 0);
                                    return "Net: " + money(net, data.currency);
                                }
                            }
                        }
                    },
                    scales: {
                        x: { grid: { display: false }, border: { display: false } },
                        y: {
                            beginAtZero: true,
                            border: { display: false },
                            grid: { color: grid },
                            ticks: { maxTicksLimit: 5, callback: function (v) { return compact(v, data.currency); } }
                        }
                    }
                }
            };
        },

        share: function (data) {
            // Up to seven categories keep their own colour; the rest fold into "Other".
            var colours = data.labels.map(function (label, i) {
                return label === "Other" ? other : palette[i % palette.length];
            });

            return {
                type: "doughnut",
                data: {
                    labels: data.labels,
                    datasets: [{ data: data.values, backgroundColor: colours, borderColor: surface, borderWidth: 2, hoverOffset: 4 }]
                },
                options: {
                    maintainAspectRatio: false,
                    cutout: "68%",
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            callbacks: {
                                label: function (c) {
                                    var total = c.dataset.data.reduce(function (a, b) { return a + b; }, 0);
                                    var share = total ? (c.parsed / total * 100).toFixed(1) : 0;
                                    return c.label + ": " + money(c.parsed, data.currency) + " (" + share + "%)";
                                }
                            }
                        }
                    }
                }
            };
        },

        line: function (data) {
            return {
                type: "line",
                data: {
                    labels: data.labels,
                    datasets: [{
                        label: data.label,
                        data: data.values,
                        borderColor: palette[0],
                        backgroundColor: palette[0],
                        borderWidth: 2,
                        pointRadius: 4,
                        pointHoverRadius: 6,
                        pointBorderColor: surface,
                        pointBorderWidth: 2,
                        tension: 0.25
                    }]
                },
                options: {
                    maintainAspectRatio: false,
                    interaction: { mode: "index", intersect: false },
                    plugins: {
                        legend: { display: false },
                        tooltip: { callbacks: { label: function (c) { return data.label + ": " + money(c.parsed.y, data.currency); } } }
                    },
                    scales: {
                        x: { grid: { display: false }, border: { display: false } },
                        y: { border: { display: false }, grid: { color: grid }, ticks: { maxTicksLimit: 5, callback: function (v) { return compact(v, data.currency); } } }
                    }
                }
            };
        }
    };

    document.querySelectorAll("canvas[data-chart]").forEach(function (canvas) {
        var build = builders[canvas.dataset.chart];
        if (!build) {
            return;
        }

        var data = JSON.parse(canvas.dataset.values);
        new Chart(canvas, build(data));

        // Legend swatches drawn in HTML next to the chart use the same colours.
        var legend = document.querySelector("[data-legend-for='" + canvas.id + "']");
        if (legend) {
            legend.querySelectorAll("[data-swatch]").forEach(function (swatch) {
                var i = Number(swatch.dataset.swatch);
                swatch.style.background = data.labels[i] === "Other" ? other : palette[i % palette.length];
            });
        }
    });

    window.MyFinancesPalette = { palette: palette, income: income, expense: expense };
})();
