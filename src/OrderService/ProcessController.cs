using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Common.Persistence;
using Common.Tx;

namespace OrderService
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProcessController : ControllerBase
    {
        private readonly FileStore<Order> _orderStore;
        private readonly FileStore<Product> _productStore;
        private readonly FileStore<Payment> _paymentStore;
        private readonly FileStore<Shipment> _shipmentStore;
        private readonly ITransactionFactory _transactionFactory;
        private readonly ILogger<ProcessController> _logger;

        public ProcessController(
            FileStore<Order> orderStore,
            FileStore<Product> productStore,
            FileStore<Payment> paymentStore,
            FileStore<Shipment> shipmentStore,
            ITransactionFactory transactionFactory,
            ILogger<ProcessController> logger)
        {
            _orderStore = orderStore;
            _productStore = productStore;
            _paymentStore = paymentStore;
            _shipmentStore = shipmentStore;
            _transactionFactory = transactionFactory;
            _logger = logger;
        }

        [HttpPost("place-order")]
        public async Task<IActionResult> PlaceOrder([FromBody] Order order)
        {
            try
            {
                using var transaction = _transactionFactory.CreateTransaction();
                
                // Enlist all resources in the transaction
                transaction.EnlistResource(_orderStore);
                transaction.EnlistResource(_paymentStore);
                transaction.EnlistResource(_shipmentStore);
                
                // Save order
                await _orderStore.SaveAsync(order.Id, order);
                
                // Create payment
                var payment = new Payment 
                { 
                    Id = Guid.NewGuid().ToString(), 
                    OrderId = order.Id,
                    Amount = order.TotalAmount,
                    Status = "Pending"
                };
                await _paymentStore.SaveAsync(payment.Id, payment);
                
                // Create shipment
                var shipment = new Shipment 
                { 
                    Id = Guid.NewGuid().ToString(), 
                    OrderId = order.Id,
                    Status = "Pending"
                };
                await _shipmentStore.SaveAsync(shipment.Id, shipment);
                
                // Commit the distributed transaction
                await transaction.CommitAsync();
                
                _logger.LogInformation("Order {OrderId} processed successfully", order.Id);
                return Ok(new { OrderId = order.Id, PaymentId = payment.Id, ShipmentId = shipment.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process order {OrderId}", order.Id);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}