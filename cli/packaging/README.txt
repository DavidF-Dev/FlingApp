Fling
=====

A lightweight tool for sending clipboard content from a Windows PC to an
Android phone over the local network.

Getting started
---------------
1. Install the Fling app on your Android phone and open it.
2. On your PC, pair with your phone:

     fling pair --discover

   Or specify the IP manually:

     fling pair <phone-ip>

3. Approve the pairing request on your phone.
4. Send content:

     fling send --clipboard --all
     fling send --text "hello" --device "Pixel"
     fling send --image screenshot.png --all
     fling send --file notes.txt --all

5. Tap the notification on your phone to copy to clipboard.

Run "fling --help" for the full list of commands and options.

This archive includes two executables:

     fling.exe    Console app - use from a terminal.
     flingw.exe   GUI-subsystem variant - use when invoked by a GUI app
                  (e.g., Greenshot, tray app) to avoid a console window flash.

Send to menu
------------
Run "fling install" to add Fling to the Windows "Send to" context menu.
Right-click any file in Explorer and choose Send to > Fling to send it.

Image files are sent as images, text files send their contents, and binary
files send the file path. Run "fling uninstall" to remove the shortcut.

Auto-discovery
--------------
Once paired, Fling automatically finds your phone on any network via UDP
broadcast. If the phone's IP changes, commands like "fling send" and
"fling status" will find it without re-pairing.

Greenshot integration
---------------------
Configure Greenshot's External Command Plugin:

     Command:   C:\path\to\flingw.exe
     Arguments: send --image "{0}" --all

Use flingw.exe (not fling.exe) to avoid a console window flash on each capture.

Troubleshooting
---------------
Enable file logging to diagnose issues (e.g., when invoked by Greenshot):

     fling config set --log true

Logs are written to %APPDATA%\Fling\fling.log. Disable with --log false.

Fling is unsigned, so Windows SmartScreen may warn ("Windows protected your PC"):
choose "More info", then "Run anyway".

Homepage: https://github.com/DavidF-Dev/FlingApp
License:  MIT (see LICENSE.txt)
