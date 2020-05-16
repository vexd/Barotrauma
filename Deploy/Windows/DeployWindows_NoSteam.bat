@ECHO OFF

cd ../../Barotrauma

cd BarotraumaClient
dotnet publish WindowsClient.csproj -c Release_NoSteam --self-contained -r win-x64 /p:Platform=x64

cd ..
cd BarotraumaServer
dotnet publish WindowsServer.csproj -c Release_NoSteam --self-contained -r win-x64 /p:Platform=x64

PAUSE
