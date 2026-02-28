dotnet tool uninstall --global CalqFramework.Dvo || true
dotnet pack -c Release --output . -p:Version=0.0.0
dotnet tool install --global --add-source . --version "0.0.0" CalqFramework.Dvo
