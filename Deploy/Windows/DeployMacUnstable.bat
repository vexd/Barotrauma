@ECHO OFF

cd ../../Barotrauma

cd BarotraumaClient
dotnet publish MacClient.csproj -c Unstable --self-contained -r osx-x64 /p:Platform=x64

cd ..
cd BarotraumaServer
dotnet publish MacServer.csproj -c Unstable --self-contained -r osx-x64 /p:Platform=x64

PAUSE
