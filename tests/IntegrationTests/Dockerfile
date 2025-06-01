FROM mcr.microsoft.com/dotnet/framework/sdk:4.8
WORKDIR /src
COPY . .
RUN nuget restore IntegrationTests.csproj
RUN msbuild IntegrationTests.csproj /p:Configuration=Release
CMD ["vstest.console.exe", "bin/Release/IntegrationTests.dll"]
