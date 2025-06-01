using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Common.Persistence;
using Common.Tx;

namespace CatalogService
{
    [ApiController]
    [Route("api/[controller]")]
    public class CatalogController : ControllerBase
    {
        private readonly FileStore<Product> _productStore;
        private readonly ITransactionFactory _transactionFactory;
        private readonly ILogger<CatalogController> _logger;

        public CatalogController(
            FileStore<Product> productStore,
            ITransactionFactory transactionFactory,
            ILogger<CatalogController> logger)
        {
            _productStore = productStore;
            _transactionFactory = transactionFactory;
            _logger = logger;
        }

        [HttpPost("add")]
        public async Task<IActionResult> Add([FromBody] Product product)
        {
            try
            {
                using var transaction = _transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(_productStore);
                
                await _productStore.SaveAsync(product.Id, product);
                
                await transaction.CommitAsync();
                
                _logger.LogInformation("Product {ProductId} added successfully", product.Id);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add product {ProductId}", product.Id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            try
            {
                var product = await _productStore.GetAsync(id);
                if (product == null)
                {
                    return NotFound();
                }
                return Ok(product);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get product {ProductId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var products = await _productStore.GetAllAsync();
                return Ok(products);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all products");
                return StatusCode(500, "Internal server error");
            }
        }
    }
}