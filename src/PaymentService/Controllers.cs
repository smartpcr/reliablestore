using Microsoft.AspNetCore.Mvc;
using ReliableStore;

namespace PaymentService
{
    [ApiController]
    [Route("api/payment")]
    public class PaymentController : ControllerBase
    {
        private static readonly FileRepository<Payment> _repo = new FileRepository<Payment>("payments.json");

        [HttpPost("charge")]
        public IActionResult Charge([FromBody] Payment payment)
        {
            using (var tx = new TransactionScope())
            {
                _repo.Add(payment, tx);
                tx.Commit();
            }
            return Ok();
        }
    }
}
