(function () {
    'use strict';

    function iconFor(revealed) {
        return revealed ? 'bi-eye' : 'bi-eye-slash';
    }

    function apply(button, field, revealed) {
        field.type = revealed ? 'text' : 'password';
        button.setAttribute('aria-pressed', revealed ? 'true' : 'false');
        button.setAttribute(
            'aria-label',
            revealed ? 'Hide password' : 'Show password');

        var icon = button.querySelector('i');

        if (icon) {
            icon.classList.remove('bi-eye', 'bi-eye-slash');
            icon.classList.add(iconFor(revealed));
        }
    }

    document.addEventListener('click', function (event) {
        var button = event.target.closest('[data-reveal]');

        if (!button) {
            return;
        }

        var field = document.getElementById(button.dataset.reveal);

        if (!field) {
            return;
        }

        apply(button, field, field.type === 'password');
    });

    document.addEventListener('submit', function (event) {
        var form = event.target;

        if (!form || typeof form.querySelectorAll !== 'function') {
            return;
        }

        form.querySelectorAll('[data-reveal]').forEach(function (button) {
            var field = document.getElementById(button.dataset.reveal);

            if (field && field.type === 'text') {
                apply(button, field, false);
            }
        });
    }, true);
}());
