@echo off
echo Publishing portable EXE (Optimized)...
dotnet publish ConvertToWebP.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false -p:EnableCompressionInSingleFile=true -o "D:\projects\convert-to-webp\Publish"
echo Done.
pause
