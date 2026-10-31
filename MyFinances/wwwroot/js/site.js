// The sidebar slides in as a drawer on narrow screens.

function toggleSidebar() {
    "use strict";

    var sidebar = document.getElementById("sidebar");
    var overlay = document.getElementById("sidebarOverlay");

    if (!sidebar || !overlay) {
        return;
    }

    sidebar.classList.toggle("active");

    if (sidebar.classList.contains("active")) {
        overlay.classList.add("active");
        document.body.style.overflow = "hidden";
    } else {
        overlay.classList.remove("active");
        document.body.style.overflow = "";
    }
}

function closeSidebar() {
    "use strict";

    var sidebar = document.getElementById("sidebar");
    var overlay = document.getElementById("sidebarOverlay");

    if (!sidebar || !overlay) {
        return;
    }

    sidebar.classList.remove("active");
    overlay.classList.remove("active");
    document.body.style.overflow = "";
}

(function () {
    "use strict";

    var KEY = "sidebarScroll";

    var nav = document.getElementById("sidebarNav");

    if (!nav) {
        return;
    }

    function remember(value) {
        try {
            window.sessionStorage.setItem(KEY, value);
        } catch (e) {
        }
    }

    function remembered() {
        try {
            return parseInt(window.sessionStorage.getItem(KEY), 10);
        } catch (e) {
            return NaN;
        }
    }

    var saved = remembered();

    if (!isNaN(saved)) {
        nav.scrollTop = saved;
    }

    var current = nav.querySelector(".nav-item.active");

    if (current) {
        var navBox = nav.getBoundingClientRect();
        var itemBox = current.getBoundingClientRect();

        if (itemBox.top < navBox.top || itemBox.bottom > navBox.bottom) {
            nav.scrollTop += (itemBox.top - navBox.top) -
                (nav.clientHeight - itemBox.height) / 2;
        }
    }

    var pending = null;

    nav.addEventListener("scroll", function () {
        if (pending !== null) {
            window.clearTimeout(pending);
        }

        pending = window.setTimeout(function () {
            pending = null;
            remember(nav.scrollTop);
        }, 120);
    });

    nav.addEventListener("click", function (event) {
        if (event.target.closest(".nav-item")) {
            remember(nav.scrollTop);
            closeSidebar();
        }
    });
})();
