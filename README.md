Experimental.

Windows:
```
winget install GitHub.cli
winget install Microsoft.DotNet.SDK.9
dotnet tool update --global CalqFramework.Dvo
```

Ubuntu/Debian:
```
sudo apt install gh -y
sudo apt-get update && sudo apt-get install -y dotnet-sdk-9.0
dotnet tool update --global CalqFramework.Dvo
```

macOS:
```
brew install gh
brew install --cask dotnet-sdk@9
dotnet tool update --global CalqFramework.Dvo
```
