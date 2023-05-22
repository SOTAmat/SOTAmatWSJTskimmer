# SOTAmatWSJTskimmer

A WSJT-X Plugin that filters reception reports and forwards potential SOTAmat messages to the SOTAmat server

You can use Visual Studio to build the code.
To publish all platforms, use a command line tool and the "Publish-All_Platforms.BAT" file on Windows. It will create the executables in a "Publish" folder.

73 de AB6D - Brian Mathews

## Build Instructions

1. Get the source code for this project: `git clone [this repository]`
2. Get the source code for M0LTE's "WsjtxUdpLib":

```
   git submodule init
   git submodule update
```

3. Open the project in Visual Studio and build (or use `dotnet` command line)
4. Once you have made any desired changes, from the command line navigate to the directory with the file `Publish-All-Platforms.bat` and execute that command.

## Acknowledgement

This program incorporates the "WsjtxUdpLib" component from Tom Fanning (M0LTE) available on Github. Thank-you Tom for your many open source contributions!
