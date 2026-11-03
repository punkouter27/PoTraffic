// pt-keys.js — app-wide keyboard shortcuts, and the native <dialog> plumbing behind
// PtDialog (the bulk-actions drawer and the shortcut help overlay).
//
// Imported on demand through IJSRuntime "import" (Layout/KeyboardShortcuts.razor,
// Components/PtDialog.razor), so index.html needs no script tag for it.
//
// Shortcuts are deliberately declarative: a page marks the element a key acts on with
// data-shortcut="<key>" and this module finds it. "/" focuses the marked field; any other
// marked key clicks the marked control. Keys no page marks ("?", "n") go to .NET.

const TYPING = 'input, textarea, select, [contenteditable=""], [contenteditable="true"], [role="textbox"], [role="combobox"]';

/** True when the keystroke belongs to whatever the user is typing into. */
function isTyping(event) {
    const target = event.target;
    if (!(target instanceof Element)) return false;
    return target.isContentEditable || target.closest(TYPING) !== null;
}

/** Rendered, not hidden by CSS, and not behind an inert/aria-hidden ancestor. */
function isUsable(el) {
    return el.getClientRects().length > 0
        && !el.closest('[inert], [aria-hidden="true"]')
        && !el.matches(':disabled, [aria-disabled="true"]');
}

function marked(key) {
    // A modal dialog makes everything outside it inert; only look inside it then.
    const modal = document.querySelector('dialog[open]:modal');
    const scope = modal ?? document;
    for (const el of scope.querySelectorAll(`[data-shortcut="${CSS.escape(key)}"]`)) {
        if (isUsable(el)) return el;
    }
    return null;
}

/**
 * Starts listening. Returns a handle whose dispose() removes the listener, so the
 * owning component can tear it down when it is disposed.
 */
export function attachShortcuts(dotnet) {
    const onKeyDown = (event) => {
        if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey || event.isComposing)
            return;
        if (isTyping(event))
            return;

        const key = event.key;

        // "?" is Shift+/ on most layouts — Shift is allowed for it alone.
        if (key === '?') {
            event.preventDefault();
            dotnet.invokeMethodAsync('OnShortcut', '?');
            return;
        }

        if (event.shiftKey)
            return;

        if (key === '/') {
            const field = marked('/');
            if (field) {
                event.preventDefault();
                field.focus();
                if (typeof field.select === 'function') field.select();
            }
            return;
        }

        if (key.length !== 1)
            return;

        const lower = key.toLowerCase();
        const control = marked(lower);
        if (control) {
            event.preventDefault();
            control.click();
            return;
        }

        // Keys with no element to act on are app-level (navigation) — but not while a
        // modal is open: a dialog owns the keyboard until it closes.
        if (lower === 'n' && !document.querySelector('dialog[open]:modal')) {
            event.preventDefault();
            dotnet.invokeMethodAsync('OnShortcut', 'n');
        }
    };

    document.addEventListener('keydown', onKeyDown);
    return {
        dispose() { document.removeEventListener('keydown', onKeyDown); }
    };
}

// ── <dialog> ───────────────────────────────────────────────────────────────────

/**
 * Wires a dialog's close (Esc, the close button, a backdrop click) back to .NET, so the
 * component's Open state can never disagree with the element's. Returns a handle with
 * dispose() that removes both listeners.
 */
export function bindDialog(dialog, dotnet) {
    const onClose = () => dotnet.invokeMethodAsync('OnDialogClosed');

    // A click whose target is the <dialog> itself landed on the ::backdrop: the panel's
    // own content always has a child element under the pointer.
    const onClick = (event) => {
        if (event.target === dialog) dialog.close();
    };

    dialog.addEventListener('close', onClose);
    dialog.addEventListener('click', onClick);
    return {
        dispose() {
            dialog.removeEventListener('close', onClose);
            dialog.removeEventListener('click', onClick);
        }
    };
}

/**
 * Opens modally. showModal() moves focus into the dialog (to [autofocus] or the first
 * focusable), makes the page behind it inert, and on close the browser hands focus back
 * to whatever had it before — the focus management a hand-rolled overlay gets wrong.
 */
export function showDialog(dialog) {
    if (dialog && !dialog.open) dialog.showModal();
}

export function closeDialog(dialog) {
    if (dialog && dialog.open) dialog.close();
}
