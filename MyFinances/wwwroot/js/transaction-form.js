// The transaction form shows only the fields its type uses, and asks
// for the amount received when a transfer crosses currencies.

(function () {
    "use strict";

    var form = document.getElementById("transaction-form");

    if (!form) {
        return;
    }

    var from = form.querySelector("#AccountId");
    var to = form.querySelector("#ToAccountId");
    var cross = form.querySelector("[data-cross-currency]");

    function currentType() {
        var checked = form.querySelector("[data-type-switch]:checked");
        return checked ? checked.value : "Expense";
    }

    function currencyOf(select) {
        var option = select && select.options[select.selectedIndex];
        return option && option.dataset.currency ? option.dataset.currency : "";
    }

    function update() {
        var type = currentType();

        form.querySelectorAll("[data-show-for]").forEach(function (element) {
            var show = element.dataset.showFor.split(",").indexOf(type) !== -1;
            element.hidden = !show;

            // Hidden fields are not checked in the browser either.
            element.querySelectorAll("input, select").forEach(function (input) {
                input.disabled = !show;
            });
        });

        form.querySelectorAll("[data-currency-of]").forEach(function (badge) {
            var select = form.querySelector("#" + badge.dataset.currencyOf);
            badge.textContent = currencyOf(select) || "—";
        });

        var crosses = type === "Transfer" &&
            currencyOf(from) && currencyOf(to) &&
            currencyOf(from) !== currencyOf(to);

        if (cross) {
            cross.hidden = !crosses;
            cross.querySelectorAll("input").forEach(function (input) {
                input.disabled = !crosses;
                input.required = !!crosses;
            });
        }
    }

    form.addEventListener("change", update);
    update();
})();
