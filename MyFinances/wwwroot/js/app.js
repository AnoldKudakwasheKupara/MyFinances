// Behaviour shared by every page.

(function () {
    "use strict";

    // A form with data-confirm asks first, in a dialog that matches the
    // app rather than the browser's own box.
    var modal = null;
    var pending = null;

    function dialog() {
        if (modal) {
            return modal;
        }

        var element = document.createElement("div");
        element.className = "modal fade";
        element.tabIndex = -1;
        element.setAttribute("aria-hidden", "true");
        element.innerHTML =
            '<div class="modal-dialog modal-dialog-centered"><div class="modal-content">' +
            '<div class="modal-header"><h2 class="modal-title h5">Are you sure?</h2>' +
            '<button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button></div>' +
            '<div class="modal-body"><p class="mb-0" data-confirm-text></p></div>' +
            '<div class="modal-footer"><button type="button" class="btn btn-light" data-bs-dismiss="modal">Cancel</button>' +
            '<button type="button" class="btn btn-danger" data-confirm-go>Yes, go ahead</button></div>' +
            "</div></div>";

        document.body.appendChild(element);

        element.querySelector("[data-confirm-go]").addEventListener("click", function () {
            var form = pending;
            pending = null;
            modal.hide();

            if (form) {
                form.dataset.confirmed = "true";
                if (form.requestSubmit) {
                    form.requestSubmit();
                } else {
                    form.submit();
                }
            }
        });

        modal = new bootstrap.Modal(element);
        modal.element = element;
        return modal;
    }

    document.addEventListener("submit", function (event) {
        var form = event.target;

        if (!form.dataset || !form.dataset.confirm || form.dataset.confirmed === "true") {
            return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();

        pending = form;
        var box = dialog();
        box.element.querySelector("[data-confirm-text]").textContent = form.dataset.confirm;
        box.show();
    }, true);
})();
