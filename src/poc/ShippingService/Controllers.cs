//-------------------------------------------------------------------------------
// <copyright file="Controllers.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace ShippingService
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Common.Persistence;
    using Common.Tx;
    [ApiController]
    [Route("api/[controller]")]
    public class ShippingController : ControllerBase
    {
        private readonly FileStore<Shipment> shipmentStore;
        private readonly ITransactionFactory transactionFactory;
        private readonly ILogger<ShippingController> logger;

        public ShippingController(
            FileStore<Shipment> shipmentStore,
            ITransactionFactory transactionFactory,
            ILogger<ShippingController> logger)
        {
            this.shipmentStore = shipmentStore;
            this.transactionFactory = transactionFactory;
            this.logger = logger;
        }

        [HttpPost("ship")]
        public async Task<IActionResult> Ship([FromBody] Shipment shipment)
        {
            try
            {
                using var transaction = this.transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(this.shipmentStore);
                
                // Set initial status if not provided
                if (string.IsNullOrEmpty(shipment.Status))
                {
                    shipment.Status = "Processing";
                }
                
                // Generate tracking number if not provided
                if (string.IsNullOrEmpty(shipment.TrackingNumber))
                {
                    shipment.TrackingNumber = $"TRK{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
                }
                
                await this.shipmentStore.SaveAsync(shipment.Id, shipment);
                
                await transaction.CommitAsync();
                
                this.logger.LogInformation("Shipment {ShipmentId} created for order {OrderId} with tracking {TrackingNumber}", 
                    shipment.Id, shipment.OrderId, shipment.TrackingNumber);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to create shipment {ShipmentId}", shipment.Id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            try
            {
                var shipment = await this.shipmentStore.GetAsync(id);
                if (shipment == null)
                {
                    return NotFound();
                }
                return Ok(shipment);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to get shipment {ShipmentId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var shipments = await this.shipmentStore.GetAllAsync();
                return Ok(shipments);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to get all shipments");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("track/{trackingNumber}")]
        public async Task<IActionResult> Track(string trackingNumber)
        {
            try
            {
                var shipments = await this.shipmentStore.GetAllAsync();
                var shipment = shipments.FirstOrDefault(s => s.TrackingNumber == trackingNumber);
                
                if (shipment == null)
                {
                    return NotFound();
                }
                return Ok(shipment);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to track shipment with tracking number {TrackingNumber}", trackingNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateStatus(string id, [FromBody] string status)
        {
            try
            {
                using var transaction = this.transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(this.shipmentStore);
                
                var shipment = await this.shipmentStore.GetAsync(id);
                if (shipment == null)
                {
                    return NotFound();
                }

                shipment.Status = status;
                await this.shipmentStore.SaveAsync(id, shipment);
                
                await transaction.CommitAsync();
                
                this.logger.LogInformation("Shipment {ShipmentId} status updated to {Status}", id, status);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to update shipment {ShipmentId} status", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] Shipment shipment)
        {
            try
            {
                using var transaction = this.transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(this.shipmentStore);
                
                var existingShipment = await this.shipmentStore.GetAsync(id);
                if (existingShipment == null)
                {
                    return NotFound();
                }

                shipment.Id = id; // Ensure the ID matches
                await this.shipmentStore.SaveAsync(id, shipment);
                
                await transaction.CommitAsync();
                
                this.logger.LogInformation("Shipment {ShipmentId} updated successfully", id);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to update shipment {ShipmentId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                using var transaction = this.transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(this.shipmentStore);
                
                var existingShipment = await this.shipmentStore.GetAsync(id);
                if (existingShipment == null)
                {
                    return NotFound();
                }

                await this.shipmentStore.DeleteAsync(id);
                
                await transaction.CommitAsync();
                
                this.logger.LogInformation("Shipment {ShipmentId} deleted successfully", id);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to delete shipment {ShipmentId}", id);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
