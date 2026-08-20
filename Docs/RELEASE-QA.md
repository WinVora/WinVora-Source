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

## Clean-machine Windows 10/11 x64 test

Use a Windows Sandbox, a disposable VM, or a separate PC without Visual Studio,
the .NET SDK, Inno Setup, or a separately installed Windows App SDK.

1. Download only the installer and `SHA256SUMS.txt` from the draft release.
2. Compare the SHA-256 checksum, install as a standard user, and start WinVora.
3. Confirm that Dashboard and System Info load without development runtimes.
4. Test WinGet with internet, without internet, with a disabled source, and after cancelling.
5. Install a harmless outdated test app, update it, and verify the completion report/history.
6. Cancel a manufacturer uninstaller and verify that WinVora does not report success.
7. Complete an uninstall and verify that residue scanning starts only afterward.
8. Start a second WinVora instance and confirm activation returns to the first window.
9. Change German/English while System Info is loaded and verify that no old-language values remain.
10. Uninstall WinVora and check Start menu, installation directory, and retained user settings.

Record Windows build, WinGet version, scaling, test result, and the anonymized
diagnostic ZIP. This section cannot be marked complete from a developer-machine
build alone.

Before packaging, run:

```powershell
.\scripts\Test-PublishReadiness.ps1 -PublishDirectory .\publish
```

The release workflow runs this check automatically and repeats it for the final
installer. A valid signature becomes mandatory automatically when signing
secrets are configured.

## Low-end performance profile

- Test with 4 CPU threads, 8 GB RAM, integrated graphics, and a 60 Hz display.
- Record the `Startphase` and `Langsame Hardwaretelemetrie` log entries for five starts.
- Leave Dashboard and expanded System Info open for ten minutes each.
- Verify that sensor ticks do not overlap, UI input remains responsive, and memory stabilizes.
- Attach only the anonymized diagnostic ZIP; do not publish raw usernames, paths, serials, or addresses.
