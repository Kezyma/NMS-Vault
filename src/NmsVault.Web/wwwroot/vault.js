/**
 * Closes an open list when the next press lands outside it.
 *
 * The panel is drawn or not drawn by the component, on a flag the component owns. What this adds
 * is the one way of closing it the component cannot see: a press somewhere else on the page.
 * Escape, choosing an option and pressing the arrow again are all its own business.
 *
 * A press rather than a blur on the control. Focus moving from the control onto one of the options
 * is not the control being left, and closing on that would make the list reachable by mouse and by
 * nothing else - and on a phone focus never moves at all.
 *
 * Rather than calling back into managed code, this presses the list's own arrow, which is the
 * button that closes it. One listener serves every list on the page, so there is no per-list state
 * to keep in step.
 */
let watchingForOutsidePress = false;

export function closeListsOnOutsidePress() {
    if (watchingForOutsidePress) {
        return;
    }

    watchingForOutsidePress = true;

    // Captured, so the press is seen before anything inside the page can stop it travelling.
    document.addEventListener('pointerdown', event => {
        for (const combo of document.querySelectorAll('.vault-combo.open')) {
            if (!combo.contains(event.target)) {
                combo.querySelector('button.toggle')?.click();
            }
        }
    }, true);
}
