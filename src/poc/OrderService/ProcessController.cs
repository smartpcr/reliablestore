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
    using Common.Persistence;
    using Common.Tx;
    [ApiController]
    [Route("api/[controller]")]
    public class ProcessController : ControllerBase
    {
        private readonly FileStore<Order> orderStore;
        private readonly FileStore<Product> productStore;
        private readonly FileStore<Payment> paymentStore;
        private readonly FileStore<Shipment> shipmentStore;
        private readonly ITransactionFactory transactionFactory;
        private readonly ILogger<ProcessController> logger;

        public ProcessController(
            FileStore<Order> orderStore,
            FileStore<Product> productStore,
            FileStore<Payment> paymentStore,
            FileStore<Shipment> shipmentStore,
            ITransactionFactory transactionFactory,
            ILogger<ProcessController> logger)
        {
            this.orderStore = orderStore;
            this.productStore = productStore;
            this.paymentStore = paymentStore;
            this.shipmentStore = shipmentStore;
            this.transactionFactory = transactionFactory;
            this.logger = logger;
        }

        [HttpPost("place-order")]
        public async Task<IActionResult> PlaceOrder([FromBody] Order order)
        {
            try
            {
                using var transaction = this.transactionFactory.CreateTransaction();
                
                // Enlist all resources in the transaction
                transaction.EnlistResource(this.orderStore);
                transaction.EnlistResource(this.paymentStore);
                transaction.EnlistResource(this.shipmentStore);
                
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
                await this.paymentStore.SaveAsync(payment.Id, payment);
                
                // Create shipment
                var shipment = new Shipment 
                { 
                    Id = Guid.NewGuid().ToString(), 
                    OrderId = order.Id,
                    Status = "Pending"
                };
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