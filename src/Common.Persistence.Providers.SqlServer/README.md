# SQL Server Persistence Provider

This provider implements persistence using Microsoft SQL Server for the ReliableStore project.

## Quick Start

### Option 1: Using Docker (Recommended for Development)

Run SQL Server in a Docker container:

```bash
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourStrong@Passw0rd" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```

### Option 2: Using Existing SQL Server

Ensure your SQL Server instance:
- Is running and accessible on the network
- Has SQL Server Authentication enabled
- Has appropriate firewall rules configured

## Configuration

Configure the provider in your `appsettings.json`:

### SQL Server Authentication
```json
{
  "Persistence": {
    "Providers": {
      "SqlServer": {
        "Name": "SqlServerProvider",
        "Host": "localhost",
        "Port": 1433,
        "DbName": "ReliableStore",
        "UserId": "sa",
        "Password": "YourStrong@Passw0rd",
        "IntegratedSecurity": false,
        "Schema": "dbo",
        "CreateTableIfNotExists": true,
        "EnableRetryLogic": true,
        "MaxRetryCount": 3,
        "ConnectTimeout": 30,
        "CommandTimeout": 30
      }
    }
  }
}
```

### Windows Authentication
```json
{
  "Persistence": {
    "Providers": {
      "SqlServer": {
        "Name": "SqlServerProvider",
        "Host": "localhost",
        "Port": 1433,
        "DbName": "ReliableStore",
        "IntegratedSecurity": true,
        "Schema": "dbo",
        "CreateTableIfNotExists": true,
        "EnableRetryLogic": true,
        "MaxRetryCount": 3,
        "ConnectTimeout": 30,
        "CommandTimeout": 30
      }
    }
  }
}
```

## Connection String Options

The provider automatically builds a connection string with:
- **TrustServerCertificate**: Set to true for development environments
- **Connect Retry**: Automatically configured based on EnableRetryLogic setting
- **Timeouts**: Configurable via ConnectTimeout and CommandTimeout

## Error Handling

The provider includes:
- Connection validation before table creation
- Detailed error messages with troubleshooting steps
- Automatic retry logic for transient failures
- Quick connection test with shorter timeout

## Common Issues

### "Value cannot be null. (Parameter 'Password')"

This error occurs when:
1. Password is not set in configuration
2. Using SQL Server Authentication without credentials

**Solution**:
- Set both `UserId` and `Password` in configuration
- OR use Windows Authentication by setting `"IntegratedSecurity": true`

### "The server was not found or was not accessible"

This error typically means:
1. SQL Server is not running
2. Network/firewall is blocking the connection
3. Incorrect host/port configuration

**Solution**: 
- Verify SQL Server is running: `docker ps` (if using Docker)
- Check connectivity: `telnet localhost 1433`
- Ensure correct host/port in configuration

### "Login failed for user"

This error means:
1. Incorrect username/password
2. SQL Server Authentication not enabled

**Solution**:
- Verify credentials match those used when starting SQL Server
- Enable SQL Server Authentication mode if using Windows instance

## Features

- Automatic schema creation
- Automatic table creation with proper indexes
- JSON serialization for entity storage
- Optimistic concurrency with ETag support
- Version tracking
- Created/Updated timestamps
- Configurable retry logic for resilience