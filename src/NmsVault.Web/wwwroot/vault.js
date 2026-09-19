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

/**
 * Hands the reader a file.
 *
 * The bytes come over as base64 rather than as a byte array, because a byte array crosses the
 * interop bridge one element at a time and these are whole ship documents. Blazor's own stream
 * reference would avoid both, but it wants the stream held open across an await, and the thing
 * on the other side is a download that may sit in a "where do you want this" dialog for as long
 * as the reader likes.
 *
 * The object URL is released on the next turn rather than straight away: revoking it in the same
 * tick as the click cancels the download in Safari, which has not finished reading it yet.
 */
export function saveFile(fileName, base64, mimeType) {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);

    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }

    const url = URL.createObjectURL(new Blob([bytes], { type: mimeType || 'application/json' }));
    const link = document.createElement('a');

    link.href = url;
    link.download = fileName;
    link.style.display = 'none';

    document.body.appendChild(link);
    link.click();
    link.remove();

    setTimeout(() => URL.revokeObjectURL(url), 0);
}

/**
 * Places a panel against the control that opened it, in viewport coordinates.
 *
 * Needed because one of the two places a download menu appears is a table row, and the table
 * scrolls sideways on a narrow screen. A box with `overflow-x: auto` clips on both axes - the
 * other axis computes to `auto` rather than staying visible - so a panel positioned against the
 * row is cut off at the table's edge. Taking it out of the flow entirely is the fix; the cost is
 * that its position has to be worked out here rather than in the stylesheet.
 *
 * Measured after the panel is already in the document, so its real height is known and it can be
 * flipped above the control when there is no room below.
 */
export function anchorPanel(panel, control) {
    const margin = 8;
    const gap = 4;

    const anchor = control.getBoundingClientRect();
    const size = panel.getBoundingClientRect();

    // The client box rather than the window, which includes the scrollbars. Clamping to the
    // window puts the last few pixels of the panel underneath one.
    const width = document.documentElement.clientWidth;
    const height = document.documentElement.clientHeight;

    let left = anchor.left;
    if (left + size.width + margin > width) {
        left = width - size.width - margin;
    }
    left = Math.max(margin, left);

    let top = anchor.bottom + gap;
    if (top + size.height + margin > height) {
        const above = anchor.top - size.height - gap;
        top = above >= margin ? above : Math.max(margin, height - size.height - margin);
    }

    panel.style.left = `${Math.round(left)}px`;
    panel.style.top = `${Math.round(top)}px`;
}

/**
 * Closes an open download menu when the page or the table under it scrolls.
 *
 * Only the download menu, because only it is positioned in viewport coordinates: a panel fixed
 * to where its control used to be is worse than no panel. The filter bar's lists move with their
 * controls and are left alone, which is how the same lists behave in the app this borrows them
 * from.
 *
 * A scroll that started inside the menu is its own option list being scrolled, not the page
 * moving underneath it.
 */
let watchingForScroll = false;

export function closeMenusOnScroll() {
    if (watchingForScroll) {
        return;
    }

    watchingForScroll = true;

    // Captured, because a scroll event on an element does not bubble.
    document.addEventListener('scroll', event => {
        for (const combo of document.querySelectorAll('.vault-download.open')) {
            if (event.target instanceof Node && combo.contains(event.target)) {
                continue;
            }
            combo.querySelector('button.toggle')?.click();
        }
    }, true);
}
