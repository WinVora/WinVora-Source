# WinVora release UI checks

Run this short matrix before a public release. The build performs static checks;
these items require Windows rendering and real keyboard input.

## Theme

- Open every page once in Light, Dark, and System mode.
- Check normal, warning, error, disabled, loading, and empty states.
- Open Settings, Changelog, update confirmation, and uninstall dialogs.

## Scaling and windows

- Test 100%, 125%, 150%, 175%, and 200% Windows scaling.
- Test the minimum window size and a maximized window.
- Move WinVora between monitors with different scaling.
- Confirm that text does not clip and scrollbars do not cover actions.

## Keyboard

- Navigate each page using Tab and Shift+Tab.
- Activate buttons and checkboxes with Enter and Space.
- Close dialogs with Escape and verify focus returns to the triggering control.
- Verify visible focus outlines on icon-only actions.

## Operations

- Start and cancel an update check, storage scan, and large-folder analysis.
- Close WinVora during each cancellable operation.
- Confirm that buttons recover after errors and repeated clicks do not start parallel work.
