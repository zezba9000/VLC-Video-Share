# VLC-Video-Share
Simple HTTP server designed around sharing Video or Audio to VLC compatible devices
* Simply copy URLs and paste them into VLC streams and it just works.
* Or download files to other devices.

## Firewall (Allow port on different OSes)
* Windows:
    * Add access rule: ```netsh http add urlacl url=http://+:8085/ user=Everyone```
    * List access rule: ```netsh http show urlacl```
    * Remove access rule: ```netsh http delete urlacl url=http://+:8080/```
    * Add firewall rule: ```netsh advfirewall firewall add rule name="Allow Port 8085" dir=in action=allow protocol=TCP localport=8085```
    * Show firewall rule: ```netsh advfirewall firewall show rule name="Allow Port 8085"```
    * Remove firewall rule: ```netsh advfirewall firewall delete rule name="Allow Port 8085"```
* macOS: ```(Nothing needed for port 8085 [needs testings])```
* Linux: ```sudo ufw allow 8085 [needs testings]```

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