Fling Tray
==========

A lightweight tool for sending clipboard content from a Windows PC to an
Android phone over the local network.

Getting started
---------------
1. Install the Fling app on your Android phone and start the service.
   Look for its "Fling is running" notification - the phone only answers
   while that service is running.

2. Run FlingTray.exe on your PC. An icon appears in the notification area,
   and the Device manager opens because nothing is paired yet.

3. Pick your phone from "Found on this network" and choose Pair. If it does
   not appear, both devices must be on the same Wi-Fi; otherwise type the
   address shown in the phone app.

4. Approve the pairing request on your phone.

5. Copy something, then run FlingTray.exe again (or double-click the tray
   icon). Whatever you copied is already staged - press Enter to send it.

6. Tap the notification on your phone to copy it to the clipboard.

Using it
--------
Running FlingTray.exe always opens the Fling window, whether it starts the
app or brings the running one forward. From there you can:

  - Send whatever you copied. It is staged automatically when the window
    opens, and text can be edited before sending.
  - Press Ctrl+V to pick up something you copied after opening the window.
  - Drag a file onto the window, or use "Choose file...".
  - Choose which device to send to, or all of them.

Fling sends things you can paste: text, rich text, and images. Files that
cannot be pasted on a phone - PDFs, archives, executables - are refused
rather than sent as a useless file path.

Content that another app marked as private, such as a password manager
entry, is not staged automatically. Press Ctrl+V to send it deliberately.

Settings
--------
Right-click the tray icon and choose Settings.

Maximum size, compression, this PC's name, and logging are shared with the
fling command line tool if you also have it installed. Notifications,
remembering the last device, starting at sign-in, and the Explorer
"Send to" entry apply to this app only.

Notifications are set to report failures only. Successful sends mark the
tray icon briefly instead of interrupting.

Command line tool
-----------------
Fling also ships a command line tool, released separately. Neither requires
the other; both share the same paired devices and settings.

  https://github.com/DavidF-Dev/FlingApp/releases

Troubleshooting
---------------
A device shows as unreachable when Fling is not running on it, or when the
two devices are on different networks. Check for the "Fling is running"
notification on the phone.

Enable logging in Settings to record each send and its outcome. The log is
written to %APPDATA%\Fling\fling.log, and Settings has a button to open it.

Fling is unsigned, so Windows SmartScreen may warn ("Windows protected your
PC"): choose "More info", then "Run anyway".

Homepage: https://github.com/DavidF-Dev/FlingApp
License:  MIT (see LICENSE.txt)
