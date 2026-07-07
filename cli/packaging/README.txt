Fling
=====

A lightweight tool for sending clipboard content from a Windows PC to an
Android phone over the local network.

Getting started
---------------
1. Install the Fling app on your Android phone and open it.
2. On your PC, pair with your phone:

     fling pair <phone-ip>

3. Approve the pairing request on your phone.
4. Send content:

     fling send --clipboard --all
     fling send --text "hello" --device "Pixel"
     fling send --image screenshot.png --all

5. Tap the notification on your phone to copy to clipboard.

Run "fling --help" for the full list of commands and options.

Fling is unsigned, so Windows SmartScreen may warn ("Windows protected your PC"):
choose "More info", then "Run anyway".

Homepage: https://github.com/DavidF-Dev/FlingApp
License:  MIT (see LICENSE.txt)
