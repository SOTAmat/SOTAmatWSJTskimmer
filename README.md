# SOTAmatSkimmer

A Plugin for both WSJT-X and SparkSDR that filters reception reports and forwards potential SOTAmat messages to the SOTAmat server, thereby reducing the normal 5-minute PSK Reporter delay.

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

## Documentation

See the PDF file in this repository, or run the app with the help option like:  `SOTAmatSkimmer --help`

## Pre-Built Executables

Found here, along with the latest documentation: [https://1drv.ms/f/s!AhZ33h8betkWju1glmGtNv0X_zCoOw?e=bL78qE](https://1drv.ms/f/c/16d97a1b1fde7716/EhZ33h8betkggBbgtgMAAAABIwhrHAmY4tlXVQulxYIf8A?e=04HCPD)

## License

Licensed under The MIT License.

## Acknowledgement

This program incorporates the "WsjtxUdpLib" component from Tom Fanning (M0LTE) available on Github. Thank-you Tom for your many open source contributions!
