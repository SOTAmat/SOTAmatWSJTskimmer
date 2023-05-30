dotnet publish -c Release -r win-x64		--self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true -o ./publish/windows-intel-64bit
dotnet publish -c Release -r linux-arm64	--self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true -o ./publish/linux-arm-64bit
dotnet publish -c Release -r linux-arm  	--self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true -o ./publish/linux-arm-32bit
dotnet publish -c Release -r linux-x64		--self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true -o ./publish/linux-intel-64bit
dotnet publish -c Release -r osx-arm64		--self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true -o ./publish/mac-osx-arm-M1-64bit
dotnet publish -c Release -r osx-x64		--self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false /p:PublishSingleFileCompression=true -o ./publish/mac-osx-intel-64bit


del .\publish\windows-intel-64bit\*.pdb
del .\publish\linux-arm-64bit\*.pdb
del .\publish\linux-arm-32bit\*.pdb
del .\publish\linux-intel-64bit\*.pdb
del .\publish\mac-osx-arm-M1-64bit\*.pdb
del .\publish\mac-osx-intel-64bit\*.pdb
