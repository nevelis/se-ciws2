Nevelis's CIWS script
=====================

This is the CIWS script, with the bare minimum code taken from my other
libraries in order for it to be functional.  It has been completely untested in
its current form, because the blueprint for my CIWS is on my old hard drive and
it's currently in a box, as I'm moving... I haven't touched the source in
months, but am uploading it so people can have a look around.  Some parts are
nicer than others. :)

In its current form, it has been adapted since scripting it in my video
(have a watch: https://www.youtube.com/watch?v=3J9RSmORYRA) to support rocket
launchers.

Requirements
------------

* Visual Studio 2019 Community Edition: https://visualstudio.microsoft.com/thank-you-downloading-visual-studio/?sku=Community
* Malware Dev Kit: https://github.com/malware-dev/MDK-SE/releases/download/1.4.7/MDK.vsix

Building
--------

* Open the CIWS2.sln file
* Tools > MDK > Deploy All Scripts
* From within Space Engineers, the script will be accessible from the "Browse
  Scripts" button from within a Programmable Block

Credits
-------

The code for GyroTurn6() and portions of the RunGuidance() has been adapted from
Rdav's missile guidance script.

Shout out to Whiplash who provided some great resources on missile guidance.
