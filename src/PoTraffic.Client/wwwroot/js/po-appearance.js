// po-appearance.js — density and its persistence.
//
// The colour theme is not the app's to set: it follows the device
// (prefers-color-scheme) through po-tokens.css, with no in-app switch.
//
// The <head> resolver in index.html stamps data-density before first paint so
// the page never flashes the wrong one. This module owns the changes made
// afterwards, from Settings.
//
// Switching wraps the attribute write in a view transition where the browser
// has one. That is a safe place to use it: the app fully controls this DOM
// mutation, it is a single attribute, and the whole page legitimately
// cross-fades. Route changes deliberately do NOT get this treatment — Blazor
// owns that render, and wrapping it would mean guessing when it lands.

const DENSITY_KEY = "potraffic:density";

function store(key, value) {
    try {
        if (value === null) localStorage.removeItem(key);
        else localStorage.setItem(key, value);
    } catch {
        // Private mode — the choice simply doesn't survive the session.
    }
}

function read(key) {
    try { return localStorage.getItem(key); } catch { return null; }
}

/** Applies `mutate` inside a view transition when one is available. */
function transition(mutate) {
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    if (reduced || typeof document.startViewTransition !== "function") {
        mutate();
        return;
    }
    document.startViewTransition(mutate);
}

/**
 * @param {"compact"|"normal"|"comfortable"} value
 */
export function setDensity(value) {
    const root = document.documentElement;

    if (value === "normal") {
        store(DENSITY_KEY, null);
        transition(() => root.removeAttribute("data-density"));
        return;
    }

    store(DENSITY_KEY, value);
    transition(() => root.setAttribute("data-density", value));
}

/** Current selections, for a settings page that has to render them as chosen. */
export function getAppearance() {
    return {
        density: read(DENSITY_KEY) ?? "normal",
        // Reported so the UI can say the OS is already asking for these rather
        // than leaving the user wondering why the app looks different.
        prefersDark: window.matchMedia("(prefers-color-scheme: dark)").matches,
        prefersContrast: window.matchMedia("(prefers-contrast: more)").matches,
        prefersReducedMotion: window.matchMedia("(prefers-reduced-motion: reduce)").matches
    };
}
