//-------------------------------------------------------------------------------
// <copyright file="Controllers.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace CatalogService
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Common.Persistence;
    using Common.Tx;
    [ApiController]
    [Route("api/[controller]")]
    public class CatalogController : ControllerBase
    {
        private readonly FileStore<Product> productStore;
        private readonly ITransactionFactory transactionFactory;
        private readonly ILogger<CatalogController> logger;

        public CatalogController(
            FileStore<Product> productStore,
            ITransactionFactory transactionFactory,
            ILogger<CatalogController> logger)
        {
            this.productStore = productStore;
            this.transactionFactory = transactionFactory;
            this.logger = logger;
        }

        [HttpPost("add")]
        public async Task<IActionResult> Add([FromBody] Product product)
        {
            try
            {
                using var transaction = this.transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(this.productStore);
                
                await this.productStore.SaveAsync(product.Id, product);
                
                await transaction.CommitAsync();
                
                this.logger.LogInformation("Product {ProductId} added successfully", product.Id);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to add product {ProductId}", product.Id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            try
            {
                var product = await this.productStore.GetAsync(id);
                if (product == null)
                {
                    return NotFound();
                }
                return Ok(product);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to get product {ProductId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var products = await this.productStore.GetAllAsync();
                return Ok(products);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to get all products");
                return StatusCode(500, "Internal server error");
            }
        }
    }
}