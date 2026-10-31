(function () {
    "use strict";

    var dialog = document.getElementById("idle-warning");
    var logoutForm = document.querySelector("form.logout-form");

    if (!dialog || !logoutForm) {
        return;
    }

    var idleMs = Number(dialog.dataset.idleSeconds) * 1000;
    var warningMs = Number(dialog.dataset.warningSeconds) * 1000;
    var keepAliveMs = Number(dialog.dataset.keepaliveSeconds) * 1000;
    var keepAliveUrl = dialog.dataset.keepaliveUrl;
    var loginUrl = dialog.dataset.loginUrl;

    var countdown = dialog.querySelector("[data-idle-countdown]");
    var activityKey = "hrportal.lastActivity";
    var signedOutKey = "hrportal.signedOutAt";

    var loadedAt = Date.now();
    var lastActivity = loadedAt;
    var lastStored = 0;
    var lastPing = loadedAt;
    var leaving = false;

    function read(key) {
        try {
            return Number(window.localStorage.getItem(key)) || 0;
        } catch (e) {
            return 0;
        }
    }

    function write(key, value) {
        try {
            window.localStorage.setItem(key, String(value));
        } catch (e) {
        }
    }

    function timedOutUrl() {
        return loginUrl +
            "?timedOut=1&ReturnUrl=" +
            encodeURIComponent(window.location.pathname + window.location.search);
    }

    function goToLogin() {
        if (leaving) {
            return;
        }

        leaving = true;
        window.location.replace(timedOutUrl());
    }

    function ping() {
        lastPing = Date.now();

        fetch(keepAliveUrl, {
            credentials: "same-origin",
            cache: "no-store",
            headers: { "X-Requested-With": "XMLHttpRequest" }
        }).then(function (response) {
            if (response.redirected || !response.ok) {
                goToLogin();
            }
        }).catch(function () {
        });
    }

    function showWarning(remainingMs) {
        if (countdown) {
            countdown.textContent = String(Math.max(0, Math.ceil(remainingMs / 1000)));
        }

        if (dialog.open) {
            return;
        }

        if (typeof dialog.showModal === "function") {
            dialog.showModal();
        } else {
            dialog.setAttribute("open", "");
        }

        var stay = dialog.querySelector("[data-idle-stay]");

        if (stay) {
            stay.focus();
        }
    }

    function hideWarning() {
        if (!dialog.open) {
            return;
        }

        if (typeof dialog.close === "function") {
            dialog.close();
        } else {
            dialog.removeAttribute("open");
        }
    }

    function markActive() {
        if (leaving) {
            return;
        }

        var now = Date.now();

        lastActivity = now;

        if (now - lastStored > 5000) {
            lastStored = now;
            write(activityKey, now);
        }

        if (now - lastPing >= keepAliveMs) {
            ping();
        }

        hideWarning();
    }

    function signOut() {
        if (leaving) {
            return;
        }

        write(signedOutKey, Date.now());

        fetch(logoutForm.action, {
            method: "POST",
            body: new FormData(logoutForm),
            credentials: "same-origin",
            redirect: "manual"
        }).catch(function () {
        }).then(goToLogin, goToLogin);
    }

    function tick() {
        if (leaving) {
            return;
        }

        if (read(signedOutKey) > loadedAt) {
            goToLogin();
            return;
        }

        var last = Math.max(lastActivity, read(activityKey));
        var idle = Date.now() - last;

        if (idle >= idleMs) {
            signOut();
        } else if (idle >= idleMs - warningMs) {
            showWarning(idleMs - idle);
        } else {
            hideWarning();
        }
    }

    ["pointerdown", "keydown", "wheel", "touchstart"].forEach(function (name) {
        document.addEventListener(name, markActive, { capture: true, passive: true });
    });

    var lastMove = 0;

    document.addEventListener("mousemove", function () {
        var now = Date.now();

        if (now - lastMove > 1000) {
            lastMove = now;
            markActive();
        }
    }, { passive: true });

    window.addEventListener("scroll", markActive, { capture: true, passive: true });

    document.addEventListener("visibilitychange", function () {
        if (!document.hidden) {
            tick();
        }
    });

    window.addEventListener("storage", function (event) {
        if (event.key === signedOutKey) {
            tick();
        }
    });

    dialog.addEventListener("cancel", function (event) {
        event.preventDefault();
        markActive();
    });

    dialog.querySelector("[data-idle-stay]").addEventListener("click", function () {
        markActive();
        ping();
    });

    dialog.querySelector("[data-idle-signout]").addEventListener("click", function () {
        write(signedOutKey, Date.now());
        leaving = true;
        logoutForm.submit();
    });

    write(activityKey, loadedAt);
    lastStored = loadedAt;

    window.setInterval(tick, 1000);
})();
