# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY *.sln ./
COPY Directory.Build.props ./
COPY Directory.Build.targets ./
COPY Directory.Packages.props ./
COPY global.json ./
COPY src/Common.Persistence/Common.Persistence.csproj ./src/Common.Persistence/
COPY src/Common.Tx/Common.Tx.csproj ./src/Common.Tx/
COPY src/poc/Models/Models.csproj ./src/poc/Models/
COPY src/poc/OrderService/OrderService.csproj ./src/poc/OrderService/

# Restore dependencies
RUN dotnet restore src/poc/OrderService/OrderService.csproj

# Copy source code
COPY src/Common.Persistence/ ./src/Common.Persistence/
COPY src/Common.Tx/ ./src/Common.Tx/
COPY src/poc/Models/ ./src/poc/Models/
COPY src/poc/OrderService/ ./src/poc/OrderService/

# Build and publish
WORKDIR /src/src/poc/OrderService
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create data directory
RUN mkdir -p /app/data

# Copy published application
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

# Expose port
EXPOSE 8080

# Create non-root user
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

ENTRYPOINT ["dotnet", "CRP.OrderService.dll"]