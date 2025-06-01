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
    public class OrderController : ControllerBase
    {
        private readonly FileStore<Order> _orderStore;
        private readonly ITransactionFactory _transactionFactory;
        private readonly ILogger<OrderController> _logger;

        public OrderController(
            FileStore<Order> orderStore,
            ITransactionFactory transactionFactory,
            ILogger<OrderController> logger)
        {
            _orderStore = orderStore;
            _transactionFactory = transactionFactory;
            _logger = logger;
        }

        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] Order order)
        {
            try
            {
                using var transaction = _transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(_orderStore);
                
                await _orderStore.SaveAsync(order.Id, order);
                
                await transaction.CommitAsync();
                
                _logger.LogInformation("Order {OrderId} created successfully", order.Id);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create order {OrderId}", order.Id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            try
            {
                var order = await _orderStore.GetAsync(id);
                if (order == null)
                {
                    return NotFound();
                }
                return Ok(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get order {OrderId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var orders = await _orderStore.GetAllAsync();
                return Ok(orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all orders");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] Order order)
        {
            try
            {
                using var transaction = _transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(_orderStore);
                
                var existingOrder = await _orderStore.GetAsync(id);
                if (existingOrder == null)
                {
                    return NotFound();
                }

                order.Id = id; // Ensure the ID matches
                await _orderStore.SaveAsync(id, order);
                
                await transaction.CommitAsync();
                
                _logger.LogInformation("Order {OrderId} updated successfully", id);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update order {OrderId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateStatus(string id, [FromBody] string status)
        {
            try
            {
                using var transaction = _transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(_orderStore);
                
                var order = await _orderStore.GetAsync(id);
                if (order == null)
                {
                    return NotFound();
                }

                order.Status = status;
                await _orderStore.SaveAsync(id, order);
                
                await transaction.CommitAsync();
                
                _logger.LogInformation("Order {OrderId} status updated to {Status}", id, status);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update order {OrderId} status", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                using var transaction = _transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(_orderStore);
                
                var existingOrder = await _orderStore.GetAsync(id);
                if (existingOrder == null)
                {
                    return NotFound();
                }

                await _orderStore.DeleteAsync(id);
                
                await transaction.CommitAsync();
                
                _logger.LogInformation("Order {OrderId} deleted successfully", id);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete order {OrderId}", id);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
