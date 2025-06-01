FROM mcr.microsoft.com/dotnet/sdk:8.0
WORKDIR /src
COPY . .
RUN dotnet restore IntegrationTests.csproj
RUN dotnet build IntegrationTests.csproj -c Release
CMD ["dotnet", "test", "--no-build", "-c", "Release"]
