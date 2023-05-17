(function () {
    'use strict';

    // Navbar helper
    (function () {
        const navLinks = document.querySelectorAll(".navbar-header .nav-link");
        navLinks.forEach((link) => {
            const href = link.getAttribute("href");
            if (href === window.location.pathname) {
                link.classList.add("active");
            }
        });
    })();

    console.log('VintageHive JavaScript Online');
})();

function gei(id) { return document.getElementById(id); }
function ce(name) { return document.createElement(name); }

async function callApi(name, data) {
    const url = '/api/' + name;
    const res = await fetch(url, data !== undefined ? { method: "POST", body: data } : undefined);
    const json = await res.json();
    return json;
}

function onOrOff(boolean) { return boolean ? "ONLINE" : "OFFLINE"; }

function formData(object) {
    let formData = new FormData();
    for (const key in object) {
        formData.append(key, object[key]);
    }
    return formData;
}

function formDataToJson(formEl) {
    let formData = new FormData(formEl);

    var object = {};
    formData.forEach((value, key) => object[key] = value);
    return JSON.stringify(object);
}