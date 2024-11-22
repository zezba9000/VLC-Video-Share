# VLC-Video-Share
Simple HTTP server designed around sharing Video or Audio to VLC compatible devices

## Firewall (Allow port on different OSes)
* Windows: ```netsh http add urlacl url=http://+:8085/ user=Everyone```
    * Windows list: ```netsh http show urlacl```
    * Windows remove: ```netsh http delete urlacl url=http://+:8080/```
* macOS: ```(Nothing needed for port 8085)```
* Linux: ```sudo ufw allow 8085```

## Building (NOTE: you can swap '*-x64' with '*-arm64' or '*-arm')
* Windows (needs .NET installed): ```dotnet publish -r win-x64 -c Release```
* Windows (doesn't need .NET installed): ```dotnet publish -r win-x64 --self-contained -c Release```
* Windows (AOT doesn't need .NET installed): ```dotnet publish -r win-x64 -c Release /p:PublishAot=true```
</br></br>
* macOS (needs .NET installed): ```dotnet publish -r osx-x64 -c Release```
* macOS (doesn't need .NET installed): ```dotnet publish -r osx-x64 --self-contained -c Release```
* macOS (AOT doesn't need .NET installed): ```dotnet publish -r osx-x64 -c Release /p:PublishAot=true```
</br></br>
* Linux (needs .NET installed): ```dotnet publish -r linux-x64 -c Release```
* Linux (doesn't need .NET installed): ```dotnet publish -r linux-x64 --self-contained -c Release```
* Linux (AOT doesn't need .NET installed): ```dotnet publish -r linux-x64 -c Release /p:PublishAot=true```