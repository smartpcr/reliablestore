//-------------------------------------------------------------------------------
// <copyright file="ProcessController.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace OrderService
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Common.Persistence.Contract;
    using Common.Persistence.Factory;
    using Common.Persistence.Transaction;
    using Common.Tx;
    using Models;

    [ApiController]
    [Route("api/[controller]")]
    public class ProcessController : ControllerBase
    {
        private readonly ICrudStorageProvider<Order> orderStore;
        private readonly ICrudStorageProvider<Product> productStore;
        private readonly ICrudStorageProvider<Payment> paymentStore;
        private readonly ICrudStorageProvider<Shipment> shipmentStore;
        private readonly ITransactionFactory transactionFactory;
        private readonly ILogger<ProcessController> logger;

        public ProcessController(
            ICrudStorageProviderFactory factory,
            ITransactionFactory transactionFactory,
            ILogger<ProcessController> logger)
        {
            this.orderStore = factory.Create<Order>(nameof(Order));
            this.productStore = factory.Create<Product>(nameof(Product));
            this.paymentStore = factory.Create<Payment>(nameof(Payment));
            this.shipmentStore = factory.Create<Shipment>(nameof(Shipment));
            this.transactionFactory = transactionFactory;
            this.logger = logger;
        }

        [HttpPost("place-order")]
        public async Task<IActionResult> PlaceOrder([FromBody] Order order)
        {
            try
            {
                await using var transaction = this.transactionFactory.CreateTransaction();

                // Enlist all resources in the transaction
                transaction.EnlistResource(new TransactionalResource<Order>(
                    order, o => o.Key, this.orderStore));

                // Save order
                await this.orderStore.SaveAsync(order.Id, order);

                // Create payment
                var payment = new Payment
                {
                    Id = Guid.NewGuid().ToString(),
                    OrderId = order.Id,
                    Amount = order.TotalAmount,
                    Status = "Pending"
                };
                transaction.EnlistResource(new TransactionalResource<Payment>(
                    payment, p => p.Key, this.paymentStore));
                await this.paymentStore.SaveAsync(payment.Id, payment);

                // Create shipment
                var shipment = new Shipment
                {
                    Id = Guid.NewGuid().ToString(),
                    OrderId = order.Id,
                    Status = "Pending"
                };

                transaction.EnlistResource(new TransactionalResource<Shipment>(
                    shipment, s => s.Key, this.shipmentStore));
                await this.shipmentStore.SaveAsync(shipment.Id, shipment);

                // Commit the distributed transaction
                await transaction.CommitAsync();

                this.logger.LogInformation("Order {OrderId} processed successfully", order.Id);
                return Ok(new { OrderId = order.Id, PaymentId = payment.Id, ShipmentId = shipment.Id });
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to process order {OrderId}", order.Id);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}