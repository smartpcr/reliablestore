# ReliableStore Proof of Concept

This repository contains a minimal proof of concept of a distributed transaction store written for .NET 8/9. It demonstrates how multiple microservices can interact with a shared file-based backend while ensuring transactional consistency.

## Projects

- **ReliableStore** – core library providing `TransactionScope` and `DistributedTransaction` for transactional file operations.
- **CatalogService** – sample microservice exposing product catalog APIs.
- **OrderService** – orchestrates orders and coordinates transactions across services.
- **CustomerService**, **PaymentService**, **ShippingService** – additional microservices using the library.
- **ReliableStore.Tests** – unit tests for the transaction library.
- **IntegrationTests** – sample integration tests run inside a container.

## Running Services

The repository includes a `docker-compose.yml` file that uses the official .NET runtime images (such as `mcr.microsoft.com/dotnet/runtime:8.0` or `9.0`). Build and run all services with:

```bash
docker-compose build
docker-compose up
```

Integration tests run in a dedicated container and depend on all services.

## Notes

- Services target **.NET 8/9** and rely on the official .NET runtime images (e.g., `mcr.microsoft.com/dotnet/runtime:8.0`).
- File-based repositories are used only for demonstration purposes and are not suitable for production.
