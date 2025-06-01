using System;
using Microsoft.AspNetCore.Mvc;
using ReliableStore;

namespace OrderService
{
    [ApiController]
    [Route("api/process")]
    public class ProcessController : ControllerBase
    {
        private static readonly FileRepository<Order> _orders = new FileRepository<Order>("orders.json");
        private static readonly FileRepository<Product> _products = new FileRepository<Product>("../CatalogService/catalog.json");
        private static readonly FileRepository<Payment> _payments = new FileRepository<Payment>("../PaymentService/payments.json");
        private static readonly FileRepository<Shipment> _shipments = new FileRepository<Shipment>("../ShippingService/shipments.json");

        [HttpPost("place-order")]
        public IActionResult PlaceOrder([FromBody] Order order)
        {
            using (var dt = new DistributedTransaction())
            {
                var tx1 = new TransactionScope();
                _orders.Add(order, tx1);
                dt.Add(tx1);

                var tx2 = new TransactionScope();
                var payment = new Payment { Id = Guid.NewGuid().ToString(), Amount = order.Quantity * 10 };
                _payments.Add(payment, tx2);
                dt.Add(tx2);

                var tx3 = new TransactionScope();
                var shipment = new Shipment { Id = Guid.NewGuid().ToString(), OrderId = order.Id };
                _shipments.Add(shipment, tx3);
                dt.Add(tx3);

                dt.Commit();
            }
            return Ok();
        }
    }
}
