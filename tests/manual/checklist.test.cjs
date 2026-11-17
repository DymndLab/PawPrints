const fs = require("node:fs"),
    path = require("node:path"),
    assert = require("node:assert/strict");
const { JSDOM, VirtualConsole } = require("jsdom");
const html = fs.readFileSync(
    path.join(__dirname, "checklist.html"),
    "utf8",
);
const KEY = "pawprints-manual-tests-v1";
let checks = 0;
function check(value, message) {
    assert.ok(value, message);
    checks++;
    console.log("PASS " + message);
}
function setup(source = html, saved = null, denyStorage = false) {
    const errors = [],
        vc = new VirtualConsole();
    vc.on("jsdomError", (e) => errors.push(String(e)));
    let blob;
    const dom = new JSDOM(source, {
        url: "https://local.test/testing-checklist.html",
        runScripts: "dangerously",
        virtualConsole: vc,
        beforeParse(w) {
            w.scrollTo = () => {};
            w.HTMLElement.prototype.scrollIntoView = () => {};
            w.confirm = () => true;
            w.Blob = class {
                constructor(parts) {
                    this.text = parts.join("");
                }
            };
            w.URL.createObjectURL = (b) => {
                blob = b;
                return "blob:test";
            };
            w.URL.revokeObjectURL = () => {};
            w.HTMLAnchorElement.prototype.click = function () {
                w.downloaded = { name: this.download, text: blob.text };
            };
            if (saved) w.localStorage.setItem(KEY, saved);
            if (denyStorage)
                Object.defineProperty(w, "localStorage", {
                    get() {
                        throw Error("denied");
                    },
                });
        },
    });
    return { dom, w: dom.window, d: dom.window.document, errors };
}
function input(w, node, value) {
    node.value = value;
    node.dispatchEvent(new w.Event("input", { bubbles: true }));
}
function change(w, node, value) {
    node.value = value;
    node.dispatchEvent(new w.Event("change", { bubbles: true }));
}
(async () => {
    const a = setup(),
        { w, d } = a;
    check(a.errors.length === 0, "Checklist starts without script errors");
    check(
        d.querySelectorAll("nav button").length === 22,
        "All twenty-one sections plus All tests are available",
    );
    d.querySelector('nav button[data-section="0"]').click();
    const count = d.querySelectorAll(".task").length;
    check(count === 251, "All 251 current checklist items are available");
    d.getElementById("check-1.1").click();
    check(
        d.getElementById("total").textContent.startsWith("1 /"),
        "Pass checkbox updates global progress",
    );
    check(
        d.querySelector(".section-progress").textContent === "1 of 7 reviewed",
        "Section counts update immediately",
    );
    change(w, d.querySelector("#test-1\\.2 select"), "fail");
    const c = d.getElementById("test-1.2");
    c.querySelector("button").click();
    const note = 'Fails on Gemini\n<script>alert("x")</script> & labels';
    input(w, c.querySelector("textarea"), note);
    check(
        JSON.parse(w.localStorage.getItem(KEY)).results["1.2"].notes === note,
        "Multiline notes autosave safely",
    );
    input(w, d.getElementById("build"), "0.5.9");
    change(w, d.getElementById("craft"), "Gemini");
    change(w, d.querySelector("#test-1\\.3 select"), "skip");
    const saved = w.localStorage.getItem(KEY);
    check(
        JSON.parse(saved).results["1.3"].build === "0.5.9" &&
            JSON.parse(saved).results["1.3"].craft === "Gemini",
        "Results record the chosen build and craft",
    );
    const b = setup(html, saved);
    check(
        b.d.getElementById("check-1.1").checked &&
            b.d.querySelector("#test-1\\.2 select").value === "fail",
        "Reopening restores pass and fail states",
    );
    check(
        b.d.querySelector("#test-1\\.2 textarea").value === note,
        "Reopening restores notes exactly",
    );
    change(w, d.getElementById("filter"), "fail");
    check(
        d.querySelectorAll(".task").length === 1,
        "Failed filter isolates issues",
    );
    change(w, d.getElementById("filter"), "all");
    d.querySelector('nav button[data-section="3"]').click();
    input(w, d.getElementById("search"), "Fails on Gemini");
    check(
        d.querySelectorAll(".task").length === 1 &&
            d.getElementById("test-1.2"),
        "Search finds notes across sections",
    );
    d.getElementById("next").click();
    check(
        d.getElementById("search").value === "" &&
            d.querySelector(".task").dataset.status === "pending",
        "Next untested opens a pending test",
    );
    d.getElementById("export").click();
    const backup = w.downloaded.text;
    check(
        JSON.parse(backup).results["1.2"].notes === note &&
            JSON.parse(backup).catalog.length === 21,
        "JSON export carries notes, results and the test catalog",
    );
    d.getElementById("portable").click();
    const portable = w.downloaded.text;
    const p = setup(portable);
    p.d.querySelector('nav button[data-section="1"]').click();
    check(
        p.d.getElementById("check-1.1").checked &&
            p.d.querySelector("#test-1\\.2 textarea").value === note,
        "Portable HTML restores results without browser storage",
    );
    check(
        p.errors.length === 0 &&
            !p.d
                .querySelector("script:not([id])")
                ?.textContent.startsWith("alert"),
        "Embedded notes cannot break out of the JSON script",
    );
    const fresh = setup();
    Object.defineProperty(fresh.d.getElementById("file"), "files", {
        configurable: true,
        value: [{ size: backup.length, text: async () => backup }],
    });
    await fresh.d.getElementById("file").onchange();
    check(
        JSON.parse(fresh.w.localStorage.getItem(KEY)).results["1.2"].notes ===
            note,
        "JSON import restores results and saves locally",
    );
    const before = fresh.w.localStorage.getItem(KEY);
    Object.defineProperty(fresh.d.getElementById("file"), "files", {
        configurable: true,
        value: [{ size: 4, text: async () => "{}" }],
    });
    await fresh.d.getElementById("file").onchange();
    check(
        fresh.w.localStorage.getItem(KEY) === before &&
            fresh.d
                .getElementById("notice")
                .textContent.startsWith("Import not applied"),
        "Invalid import leaves existing progress untouched",
    );
    const denied = setup(html, null, true);
    denied.d.getElementById("check-1.1").click();
    denied.d.getElementById("export").click();
    check(
        JSON.parse(denied.w.downloaded.text).results["1.1"].status === "pass",
        "Blocked browser storage still allows working and exporting",
    );
    const old = JSON.parse(saved);
    old.section = 9;
    old.results["9.1"] = {
        status: "fail",
        notes: "Old auto-label result",
        build: "0.5.8",
        craft: "Rocket",
        updated: "2026-11-13T12:00:00Z",
    };
    const migrated = setup(html, JSON.stringify(old));
    check(
        migrated.d.getElementById("check-1.1").checked &&
            migrated.d.querySelector("nav button.active").dataset.section ===
                "1",
        "Retired section falls back without losing active results",
    );
    check(
        migrated.d.getElementById("total").textContent.startsWith("3 /") &&
            migrated.d
                .getElementById("archiveState")
                .textContent.startsWith("1 saved result"),
        "Retired results are retained but excluded from current progress",
    );
    migrated.d.getElementById("export").click();
    assert.deepEqual(
        JSON.parse(migrated.w.downloaded.text).results["9.1"],
        old.results["9.1"],
    );
    checks++;
    check(
        !migrated.d.querySelector('nav button[data-section="9"]'),
        "Removed feature is not offered as a current test",
    );
    migrated.d.querySelector('nav button[data-section="8"]').click();
    migrated.d.getElementById("following").click();
    check(
        migrated.d.querySelector("nav button.active").dataset.section ===
            "10" && migrated.d.getElementById("test-10.1"),
        "Next section skips the retired section and preserves later IDs",
    );
    migrated.d.getElementById("previous").click();
    check(
        migrated.d.querySelector("nav button.active").dataset.section === "8",
        "Previous section skips the retired section",
    );
    migrated.d.getElementById("portable").click();
    const archivedPortable = setup(migrated.w.downloaded.text);
    archivedPortable.d.getElementById("export").click();
    assert.deepEqual(
        JSON.parse(archivedPortable.w.downloaded.text).results["9.1"],
        old.results["9.1"],
    );
    checks++;
    const unknown = { ...old, results: { 999.1: old.results["9.1"] } };
    const unknownBefore = fresh.w.localStorage.getItem(KEY);
    Object.defineProperty(fresh.d.getElementById("file"), "files", {
        configurable: true,
        value: [{ size: 1000, text: async () => JSON.stringify(unknown) }],
    });
    await fresh.d.getElementById("file").onchange();
    check(
        fresh.w.localStorage.getItem(KEY) === unknownBefore &&
            fresh.d
                .getElementById("notice")
                .textContent.includes("Unknown test"),
        "Unknown IDs cannot overwrite saved progress",
    );
    for (const instance of [
        a,
        b,
        p,
        fresh,
        denied,
        migrated,
        archivedPortable,
    ]) {
        check(
            instance.errors.length === 0,
            "No runtime errors during interaction",
        );
        instance.dom.window.close();
    }
    console.log(`${checks} checks passed; ${count} manual tests.`);
})().catch((e) => {
    console.error(e);
    process.exitCode = 1;
});
