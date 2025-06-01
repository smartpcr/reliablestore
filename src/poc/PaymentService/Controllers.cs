//-------------------------------------------------------------------------------
// <copyright file="Controllers.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace PaymentService
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Common.Persistence;
    using Common.Tx;
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly FileStore<Payment> paymentStore;
        private readonly ITransactionFactory transactionFactory;
        private readonly ILogger<PaymentController> logger;

        public PaymentController(
            FileStore<Payment> paymentStore,
            ITransactionFactory transactionFactory,
            ILogger<PaymentController> logger)
        {
            this.paymentStore = paymentStore;
            this.transactionFactory = transactionFactory;
            this.logger = logger;
        }

        [HttpPost("charge")]
        public async Task<IActionResult> Charge([FromBody] Payment payment)
        {
            try
            {
                using var transaction = this.transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(this.paymentStore);
                
                // Set initial status if not provided
                if (string.IsNullOrEmpty(payment.Status))
                {
                    payment.Status = "Processing";
                }
                
                await this.paymentStore.SaveAsync(payment.Id, payment);
                
                await transaction.CommitAsync();
                
                this.logger.LogInformation("Payment {PaymentId} charged successfully for order {OrderId}", payment.Id, payment.OrderId);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to charge payment {PaymentId}", payment.Id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            try
            {
                var payment = await this.paymentStore.GetAsync(id);
                if (payment == null)
                {
                    return NotFound();
                }
                return Ok(payment);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to get payment {PaymentId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var payments = await this.paymentStore.GetAllAsync();
                return Ok(payments);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to get all payments");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateStatus(string id, [FromBody] string status)
        {
            try
            {
                using var transaction = this.transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(this.paymentStore);
                
                var payment = await this.paymentStore.GetAsync(id);
                if (payment == null)
                {
                    return NotFound();
                }

                payment.Status = status;
                await this.paymentStore.SaveAsync(id, payment);
                
                await transaction.CommitAsync();
                
                this.logger.LogInformation("Payment {PaymentId} status updated to {Status}", id, status);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to update payment {PaymentId} status", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("refund")]
        public async Task<IActionResult> Refund([FromBody] Payment refundPayment)
        {
            try
            {
                using var transaction = this.transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(this.paymentStore);
                
                // Create a refund payment record
                refundPayment.Status = "Refunded";
                refundPayment.Amount = -Math.Abs(refundPayment.Amount); // Negative amount for refund
                
                await this.paymentStore.SaveAsync(refundPayment.Id, refundPayment);
                
                await transaction.CommitAsync();
                
                this.logger.LogInformation("Refund {PaymentId} processed successfully for order {OrderId}", refundPayment.Id, refundPayment.OrderId);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to process refund {PaymentId}", refundPayment.Id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                using var transaction = this.transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(this.paymentStore);
                
                var existingPayment = await this.paymentStore.GetAsync(id);
                if (existingPayment == null)
                {
                    return NotFound();
                }

                await this.paymentStore.DeleteAsync(id);
                
                await transaction.CommitAsync();
                
                this.logger.LogInformation("Payment {PaymentId} deleted successfully", id);
                return Ok();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to delete payment {PaymentId}", id);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
