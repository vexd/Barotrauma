#!/bin/sh

cd ../../Barotrauma

cd BarotraumaClient
dotnet publish WindowsClient.csproj -c Unstable -clp:"ErrorsOnly;Summary" --self-contained -r win-x64 \/p:Platform="x64"

cd ..
cd BarotraumaServer
dotnet publish WindowsServer.csproj -c Unstable -clp:"ErrorsOnly;Summary" --self-contained -r win-x64 \/p:Platform="x64"
