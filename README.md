# ReliableStore - Distributed Transaction Management

This repository demonstrates a modern .NET 9.0 distributed transaction management solution with microservices showcasing transactional consistency across file-based storage.

## Architecture

The solution consists of two core libraries and five microservices:

### Core Libraries
- **Common.Persistence** - File-based storage with `FileStore<T>` implementation and entity definitions
- **Common.Tx** - Advanced distributed transaction management with ACID guarantees

### Microservices
- **CatalogService** (Port 9001) - Product catalog management
- **CustomerService** (Port 9002) - Customer information management  
- **OrderService** (Port 9003) - Order orchestration and distributed transaction coordination
- **PaymentService** (Port 9004) - Payment processing with transaction support
- **ShippingService** (Port 9005) - Shipment management with tracking

## Key Features

- ✅ **Distributed Transactions** - ACID compliance across multiple services
- ✅ **File-Based Storage** - Thread-safe JSON persistence with in-memory caching
- ✅ **Modern ASP.NET Core** - .NET 9.0 with Microsoft.Extensions.Hosting
- ✅ **Docker Support** - Multi-stage builds with security best practices
- ✅ **CI/CD Pipelines** - GitHub Actions for build, test, and container publishing
- ✅ **Cross-Service Coordination** - OrderService demonstrates distributed transactions

## Quick Start

### Using Docker Compose (Recommended)

```bash
# Build and run all services
docker-compose up --build

# Run in background
docker-compose up -d

# View logs for specific service
docker-compose logs -f catalog-service

# Stop all services
docker-compose down
```

Services will be available at:
- CatalogService: http://localhost:9001
- CustomerService: http://localhost:9002  
- OrderService: http://localhost:9003
- PaymentService: http://localhost:9004
- ShippingService: http://localhost:9005

### Using .NET CLI

```bash
# Build solution
dotnet build

# Run tests
dotnet test

# Start individual service
dotnet run --project src/CatalogService
```

## Example Usage

### Creating a Complete Order (Distributed Transaction)

```bash
# 1. Add a product to catalog
curl -X POST http://localhost:9001/api/catalog/add \
  -H "Content-Type: application/json" \
  -d '{"id":"prod-1","name":"Sample Product","quantity":100,"price":29.99}'

# 2. Add a customer
curl -X POST http://localhost:9002/api/customer/add \
  -H "Content-Type: application/json" \
  -d '{"id":"cust-1","name":"John Doe","email":"john@example.com"}'

# 3. Place an order (creates order, payment, and shipment in single transaction)
curl -X POST http://localhost:9003/api/process/place-order \
  -H "Content-Type: application/json" \
  -d '{"id":"order-1","customerId":"cust-1","productId":"prod-1","quantity":2,"totalAmount":59.98}'
```

## Development

### Prerequisites
- .NET 9.0 SDK
- Docker (optional, for containerized development)

### Project Structure
```
src/
├── Common.Persistence/     # File storage and entities
├── Common.Tx/             # Transaction management
├── CatalogService/        # Product catalog APIs
├── CustomerService/       # Customer management APIs
├── OrderService/          # Order coordination and distributed transactions
├── PaymentService/        # Payment processing APIs
└── ShippingService/       # Shipping and tracking APIs

.github/workflows/
├── ci-cd.yml             # Build, test, and publish
└── docker-build.yml      # Docker image builds
```

### GitHub Actions

The repository includes automated CI/CD pipelines:

- **Continuous Integration**: Builds, tests, and publishes artifacts on every push
- **Docker Publishing**: Builds and pushes container images to GitHub Container Registry
- **Multi-platform Support**: Creates images for both AMD64 and ARM64 architectures

### Data Persistence

Each service stores data in JSON files:
- Products: `data/products.json`
- Customers: `data/customers.json`
- Orders: `data/orders.json`
- Payments: `data/payments.json`
- Shipments: `data/shipments.json`

## Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test src/Common.Tx.Tests
```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Ensure all tests pass: `dotnet test`
6. Submit a pull request

## License

This project is a proof of concept for educational purposes.