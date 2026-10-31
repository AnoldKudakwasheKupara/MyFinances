// Small comfort layer shared by every layout:
//  - a thin progress bar while the next page loads,
//  - submit buttons that show a spinner and refuse a second click,
//  - lists that stack into cards on a phone instead of scrolling sideways.

(function () {
    "use strict";

    var reduceMotion = window.matchMedia &&
        window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    // ---- Progress bar -------------------------------------------------

    var bar = document.createElement("div");
    bar.className = "ux-progress";
    bar.setAttribute("aria-hidden", "true");
    document.body.appendChild(bar);

    function showProgress() {
        if (reduceMotion) {
            return;
        }

        // Force a layout so the width transition starts from 0.
        bar.classList.remove("active");
        void bar.offsetWidth;
        bar.classList.add("active");
    }

    function hideProgress() {
        bar.classList.remove("active");
    }

    window.addEventListener("beforeunload", showProgress);

    // Coming back through the back/forward cache: clear everything.
    window.addEventListener("pageshow", function (event) {
        hideProgress();
        resetSubmitting();
    });

    // ---- Submit feedback ----------------------------------------------

    function submitButtonsOf(form) {
        var inside = Array.prototype.slice.call(
            form.querySelectorAll("button[type=submit], input[type=submit], button:not([type])"));

        if (form.id) {
            var outside = document.querySelectorAll(
                "button[form='" + form.id + "'], input[type=submit][form='" + form.id + "']");
            inside = inside.concat(Array.prototype.slice.call(outside));
        }

        return inside;
    }

    function resetSubmitting() {
        Array.prototype.forEach.call(
            document.querySelectorAll(".is-submitting"),
            function (button) {
                button.classList.remove("is-submitting");
                button.removeAttribute("aria-busy");
                if (button.dataset.uxDisabled === "1") {
                    button.disabled = false;
                    delete button.dataset.uxDisabled;
                }
            });
    }

    document.addEventListener("submit", function (event) {
        var form = event.target;

        if (!(form instanceof HTMLFormElement)) {
            return;
        }

        // Let jQuery validation (bound on the form itself) run first; if it
        // cancelled the submit, leave the buttons alone.
        window.setTimeout(function () {
            if (event.defaultPrevented) {
                return;
            }

            // Anything that opens a file in a new tab is not a navigation.
            if (form.target && form.target !== "_self") {
                return;
            }

            var buttons = submitButtonsOf(form);
            var clicked = event.submitter || null;

            buttons.forEach(function (button) {
                if (button === clicked || (!clicked && button === buttons[0])) {
                    button.classList.add("is-submitting");
                    button.setAttribute("aria-busy", "true");
                }

                // Disabling after the form data set was built keeps the
                // clicked button's name/value in the request.
                if (!button.disabled) {
                    button.disabled = true;
                    button.dataset.uxDisabled = "1";
                }
            });

            showProgress();
        }, 0);
    });

    // ---- Stacked tables on phones -------------------------------------

    function labelTables() {
        var wrappers = document.querySelectorAll(".table-responsive:not([data-no-stack])");

        Array.prototype.forEach.call(wrappers, function (wrapper) {
            var table = wrapper.querySelector(":scope > table");

            if (!table || table.classList.contains("table-stack")) {
                return;
            }

            var headerRow = table.tHead && table.tHead.rows[0];

            if (!headerRow || headerRow.cells.length < 3) {
                return;
            }

            var labels = Array.prototype.map.call(headerRow.cells, function (cell) {
                return cell.textContent.replace(/\s+/g, " ").trim();
            });

            Array.prototype.forEach.call(table.tBodies, function (body) {
                Array.prototype.forEach.call(body.rows, function (row) {
                    var column = 0;

                    Array.prototype.forEach.call(row.cells, function (cell) {
                        var span = cell.colSpan || 1;

                        if (span === 1) {
                            var label = labels[column] || "";

                            if (label) {
                                cell.setAttribute("data-label", label);
                            }

                            if (/^actions?$/i.test(label) || cell.querySelector(".btn, .btn-group")) {
                                cell.classList.add("stack-actions");
                            }
                        }

                        column += span;
                    });
                });
            });

            table.classList.add("table-stack");
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", labelTables);
    } else {
        labelTables();
    }
})();
