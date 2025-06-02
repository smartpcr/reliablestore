//-------------------------------------------------------------------------------
// <copyright file="Controllers.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace CustomerService
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
    public class CustomerController : ControllerBase
    {
        private readonly ICrudStorageProvider<Customer> customerStore;
        private readonly ITransactionFactory transactionFactory;
        private readonly ILogger<CustomerController> logger;

        public CustomerController(
            ICrudStorageProviderFactory storeFactory,
            ITransactionFactory transactionFactory,
            ILogger<CustomerController> logger)
        {
            this.customerStore = storeFactory.Create<Customer>(nameof(Customer));
            this.transactionFactory = transactionFactory;
            this.logger = logger;
        }

        [HttpPost("add")]
        public async Task<IActionResult> Add([FromBody] Customer customer)
        {
            try
            {
                await using var transaction = this.transactionFactory.CreateTransaction();
                var customerResource = new TransactionalResource<Customer>(
                    customer,
                    c => c.Key,
                    this.customerStore);
                transaction.EnlistResource(customerResource);
                await this.customerStore.SaveAsync(customer.Id, customer);
                await transaction.CommitAsync();

                this.logger.LogInformation("Customer {CustomerId} added successfully", customer.Id);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to add customer {CustomerId}", customer.Id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            try
            {
                var customer = await this.customerStore.GetAsync(id);
                if (customer == null)
                {
                    return NotFound();
                }
                return Ok(customer);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to get customer {CustomerId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var customers = await this.customerStore.GetAllAsync();
                return Ok(customers);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to get all customers");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] Customer customer)
        {
            try
            {
                await using var transaction = this.transactionFactory.CreateTransaction();
                var customerResource = new TransactionalResource<Customer>(
                    customer,
                    c => c.Key,
                    this.customerStore);
                transaction.EnlistResource(customerResource);
                var existingCustomer = await this.customerStore.GetAsync(id);
                if (existingCustomer == null)
                {
                    return NotFound();
                }

                customer.Id = id; // Ensure the ID matches
                await this.customerStore.SaveAsync(id, customer);

                await transaction.CommitAsync();

                this.logger.LogInformation("Customer {CustomerId} updated successfully", id);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to update customer {CustomerId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                await using var transaction = this.transactionFactory.CreateTransaction();
                var existingCustomer = await this.customerStore.GetAsync(id);
                if (existingCustomer == null)
                {
                    return NotFound();
                }

                var customerResource = new TransactionalResource<Customer>(
                    existingCustomer,
                    c => c.Key,
                    this.customerStore);
                transaction.EnlistResource(customerResource);
                await this.customerStore.DeleteAsync(id);

                await transaction.CommitAsync();

                this.logger.LogInformation("Customer {CustomerId} deleted successfully", id);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to delete customer {CustomerId}", id);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
