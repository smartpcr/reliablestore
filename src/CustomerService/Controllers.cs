using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Common.Persistence;
using Common.Tx;

namespace CustomerService
{
    [ApiController]
    [Route("api/[controller]")]
    public class CustomerController : ControllerBase
    {
        private readonly FileStore<Customer> _customerStore;
        private readonly ITransactionFactory _transactionFactory;
        private readonly ILogger<CustomerController> _logger;

        public CustomerController(
            FileStore<Customer> customerStore,
            ITransactionFactory transactionFactory,
            ILogger<CustomerController> logger)
        {
            _customerStore = customerStore;
            _transactionFactory = transactionFactory;
            _logger = logger;
        }

        [HttpPost("add")]
        public async Task<IActionResult> Add([FromBody] Customer customer)
        {
            try
            {
                using var transaction = _transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(_customerStore);
                
                await _customerStore.SaveAsync(customer.Id, customer);
                
                await transaction.CommitAsync();
                
                _logger.LogInformation("Customer {CustomerId} added successfully", customer.Id);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add customer {CustomerId}", customer.Id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            try
            {
                var customer = await _customerStore.GetAsync(id);
                if (customer == null)
                {
                    return NotFound();
                }
                return Ok(customer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get customer {CustomerId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var customers = await _customerStore.GetAllAsync();
                return Ok(customers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all customers");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] Customer customer)
        {
            try
            {
                using var transaction = _transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(_customerStore);
                
                var existingCustomer = await _customerStore.GetAsync(id);
                if (existingCustomer == null)
                {
                    return NotFound();
                }

                customer.Id = id; // Ensure the ID matches
                await _customerStore.SaveAsync(id, customer);
                
                await transaction.CommitAsync();
                
                _logger.LogInformation("Customer {CustomerId} updated successfully", id);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update customer {CustomerId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                using var transaction = _transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(_customerStore);
                
                var existingCustomer = await _customerStore.GetAsync(id);
                if (existingCustomer == null)
                {
                    return NotFound();
                }

                await _customerStore.DeleteAsync(id);
                
                await transaction.CommitAsync();
                
                _logger.LogInformation("Customer {CustomerId} deleted successfully", id);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete customer {CustomerId}", id);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
