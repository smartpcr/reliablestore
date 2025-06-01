//-------------------------------------------------------------------------------
// <copyright file="Controllers.cs" company="Microsoft Corp.">
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
    public class OrderController : ControllerBase
    {
        private readonly FileStore<Order> orderStore;
        private readonly ITransactionFactory transactionFactory;
        private readonly ILogger<OrderController> logger;

        public OrderController(
            FileStore<Order> orderStore,
            ITransactionFactory transactionFactory,
            ILogger<OrderController> logger)
        {
            this.orderStore = orderStore;
            this.transactionFactory = transactionFactory;
            this.logger = logger;
        }

        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] Order order)
        {
            try
            {
                using var transaction = this.transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(this.orderStore);
                
                await this.orderStore.SaveAsync(order.Id, order);
                
                await transaction.CommitAsync();
                
                this.logger.LogInformation("Order {OrderId} created successfully", order.Id);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to create order {OrderId}", order.Id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            try
            {
                var order = await this.orderStore.GetAsync(id);
                if (order == null)
                {
                    return NotFound();
                }
                return Ok(order);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to get order {OrderId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var orders = await this.orderStore.GetAllAsync();
                return Ok(orders);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to get all orders");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] Order order)
        {
            try
            {
                using var transaction = this.transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(this.orderStore);
                
                var existingOrder = await this.orderStore.GetAsync(id);
                if (existingOrder == null)
                {
                    return NotFound();
                }

                order.Id = id; // Ensure the ID matches
                await this.orderStore.SaveAsync(id, order);
                
                await transaction.CommitAsync();
                
                this.logger.LogInformation("Order {OrderId} updated successfully", id);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to update order {OrderId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateStatus(string id, [FromBody] string status)
        {
            try
            {
                using var transaction = this.transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(this.orderStore);
                
                var order = await this.orderStore.GetAsync(id);
                if (order == null)
                {
                    return NotFound();
                }

                order.Status = status;
                await this.orderStore.SaveAsync(id, order);
                
                await transaction.CommitAsync();
                
                this.logger.LogInformation("Order {OrderId} status updated to {Status}", id, status);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to update order {OrderId} status", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                using var transaction = this.transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(this.orderStore);
                
                var existingOrder = await this.orderStore.GetAsync(id);
                if (existingOrder == null)
                {
                    return NotFound();
                }

                await this.orderStore.DeleteAsync(id);
                
                await transaction.CommitAsync();
                
                this.logger.LogInformation("Order {OrderId} deleted successfully", id);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to delete order {OrderId}", id);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
