using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Common.Persistence;
using Common.Tx;

namespace PaymentService
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly FileStore<Payment> _paymentStore;
        private readonly ITransactionFactory _transactionFactory;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            FileStore<Payment> paymentStore,
            ITransactionFactory transactionFactory,
            ILogger<PaymentController> logger)
        {
            _paymentStore = paymentStore;
            _transactionFactory = transactionFactory;
            _logger = logger;
        }

        [HttpPost("charge")]
        public async Task<IActionResult> Charge([FromBody] Payment payment)
        {
            try
            {
                using var transaction = _transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(_paymentStore);
                
                // Set initial status if not provided
                if (string.IsNullOrEmpty(payment.Status))
                {
                    payment.Status = "Processing";
                }
                
                await _paymentStore.SaveAsync(payment.Id, payment);
                
                await transaction.CommitAsync();
                
                _logger.LogInformation("Payment {PaymentId} charged successfully for order {OrderId}", payment.Id, payment.OrderId);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to charge payment {PaymentId}", payment.Id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            try
            {
                var payment = await _paymentStore.GetAsync(id);
                if (payment == null)
                {
                    return NotFound();
                }
                return Ok(payment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get payment {PaymentId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var payments = await _paymentStore.GetAllAsync();
                return Ok(payments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all payments");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateStatus(string id, [FromBody] string status)
        {
            try
            {
                using var transaction = _transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(_paymentStore);
                
                var payment = await _paymentStore.GetAsync(id);
                if (payment == null)
                {
                    return NotFound();
                }

                payment.Status = status;
                await _paymentStore.SaveAsync(id, payment);
                
                await transaction.CommitAsync();
                
                _logger.LogInformation("Payment {PaymentId} status updated to {Status}", id, status);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update payment {PaymentId} status", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("refund")]
        public async Task<IActionResult> Refund([FromBody] Payment refundPayment)
        {
            try
            {
                using var transaction = _transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(_paymentStore);
                
                // Create a refund payment record
                refundPayment.Status = "Refunded";
                refundPayment.Amount = -Math.Abs(refundPayment.Amount); // Negative amount for refund
                
                await _paymentStore.SaveAsync(refundPayment.Id, refundPayment);
                
                await transaction.CommitAsync();
                
                _logger.LogInformation("Refund {PaymentId} processed successfully for order {OrderId}", refundPayment.Id, refundPayment.OrderId);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process refund {PaymentId}", refundPayment.Id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                using var transaction = _transactionFactory.CreateTransaction();
                
                transaction.EnlistResource(_paymentStore);
                
                var existingPayment = await _paymentStore.GetAsync(id);
                if (existingPayment == null)
                {
                    return NotFound();
                }

                await _paymentStore.DeleteAsync(id);
                
                await transaction.CommitAsync();
                
                _logger.LogInformation("Payment {PaymentId} deleted successfully", id);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete payment {PaymentId}", id);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
